using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Api.Telegram;
using Telegram.Bot.Exceptions;

namespace PartyBot.Api.Delivery;

public sealed class PhotoDelivery(BotDbContext db, CommandGate gate, IMediaTelegramGateway gateway,
    TimeProvider time, IOptions<BotOptions> options, ILogger<PhotoDelivery> logger)
{
    public async Task<bool> DeliverNextAsync(CancellationToken ct = default)
    {
        // Serializes the publication of buttons/refs with decisions. No DB transaction is held during network I/O.
        // Each call performs one bounded delivery step, then releases the command gate.
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            var actions = await db.OutgoingActions.Where(x => x.Kind != DeliveryKind.Text && !x.Done && !x.Failed && x.RetryAt <= now)
                .OrderBy(x => x.Id).ToListAsync(ct);
            foreach (var action in actions)
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    if (!await ExecuteAsync(action, deadline.Token)) continue; // unmet dependency; do not starve independent submissions
                    action.Done = true;
                }
                catch (Exception error) when (error is ApiRequestException or HttpRequestException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    action.Attempts++;
                    action.Failed = error is ApiRequestException { ErrorCode: 400 or 401 or 403 };
                    action.LastError = error is ApiRequestException api ? $"Telegram {api.ErrorCode}" : error.GetType().Name;
                    var delay = error is ApiRequestException { Parameters.RetryAfter: { } retry } ? retry : Math.Min(300, 2 << Math.Min(action.Attempts, 7));
                    action.RetryAt = time.GetUtcNow().AddSeconds(Math.Max(1, delay));
                    logger.LogWarning("Delivery {ActionId} ({Kind}) failed: {Error}; permanent: {Permanent}.", action.Id, action.Kind, action.LastError, action.Failed);
                    if ((action.Attempts == 1 || action.Failed) && options.Value.SuperAdminUserId > 0)
                        db.OutgoingActions.Add(new OutgoingAction
                        {
                            LogicalKey = $"delivery:{action.Id}:alert:{action.Failed}", ChatId = options.Value.SuperAdminUserId, RetryAt = now,
                            Payload = JsonSerializer.Serialize(new BotReply($"Проблема доставки #{action.Id} ({action.Kind}), объект {action.EntityId:N}: {action.LastError}. " +
                                (action.Failed ? "Нужна проверка прав/конфигурации." : "Запланирован повтор.")))
                        });
                }
                await db.SaveChangesAsync(ct);
                return true;
            }
            return false;
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<bool> ExecuteAsync(OutgoingAction action, CancellationToken ct)
    {
        if (action.Kind == DeliveryKind.CandidatePrompt) return await CandidatePromptAsync(action, ct);
        if (action.Kind == DeliveryKind.RejectPrompt) return await RejectPromptAsync(action, ct);
        var submission = await db.Submissions.Include(x => x.Media).SingleAsync(x => x.Id == action.EntityId, ct);
        var media = submission.Media.OrderBy(x => x.Order).ToArray();
        var archived = media.All(x => x.ArchiveMessageId != null);
        var terminal = submission.Status != SubmissionState.Pending;
        switch (action.Kind)
        {
            case DeliveryKind.ArchiveMedia:
                if (archived) return true;
                await SendMediaAsync(action, submission, media, false, ct);
                return true;
            case DeliveryKind.ArchiveCard:
                if (!archived) return false;
                if (submission.ArchiveCardId is null) submission.ArchiveCardId = await SendCardAsync(submission, false, ct);
                return true;
            case DeliveryKind.WorkMedia:
                if (terminal || media.All(x => x.WorkMessageId != null)) return true;
                if (!archived || submission.ArchiveCardId is null) return false;
                await SendMediaAsync(action, submission, media, true, ct);
                return true;
            case DeliveryKind.WorkCard:
                if (!await FinishedAsync(submission.Id, DeliveryKind.WorkMedia, ct)) return false;
                if (media.Any(x => x.WorkMessageId is null)) return true;
                if (submission.WorkCardId is null) submission.WorkCardId = await SendCardAsync(submission, true, ct);
                return true;
            case DeliveryKind.RefreshArchive:
                if (submission.ArchiveCardId is null) return false;
                await gateway.EditCardAsync(submission.ArchiveChatId, submission.ArchiveCardId.Value, Card(submission, false), ct);
                return true;
            case DeliveryKind.RefreshWork:
                if (submission.WorkCardId is { } workId && !await IsDeletedAsync(submission.WorkChatId, workId, ct))
                    await gateway.EditCardAsync(submission.WorkChatId, workId, Card(submission, false), ct);
                return true; // A later WorkCard is rendered from the current terminal state.
            case DeliveryKind.CleanupWork:
                if (!terminal || !archived || submission.ArchiveCardId is null ||
                    !await FinishedAsync(submission.Id, DeliveryKind.WorkCard, ct) || !await FinishedAsync(submission.Id, DeliveryKind.WorkMedia, ct)) return false;
                // Remove buttons/mark outcome before deletion: even permanently undeletable cards are harmless.
                if (submission.WorkCardId is { } cardId && !await IsDeletedAsync(submission.WorkChatId, cardId, ct))
                    await gateway.EditCardAsync(submission.WorkChatId, cardId, Card(submission, false), ct);
                foreach (var message in await db.DeliveryMessages.Where(x => x.SubmissionId == submission.Id && x.IsWork && !x.Deleted).OrderBy(x => x.Id).ToListAsync(ct))
                {
                    await gateway.DeleteAsync(message.ChatId, message.MessageId, ct);
                    message.Deleted = true;
                    await db.SaveChangesAsync(ct); // resume remaining deletions after crash, never repeat the decision
                }
                return true;
            default: throw new InvalidOperationException("Unknown delivery kind.");
        }
    }

    private Task<bool> FinishedAsync(Guid id, DeliveryKind kind, CancellationToken ct) =>
        db.OutgoingActions.AnyAsync(x => x.EntityId == id && x.Kind == kind && (x.Done || x.Failed), ct);
    private Task<bool> IsDeletedAsync(long chatId, int messageId, CancellationToken ct) =>
        db.DeliveryMessages.AnyAsync(x => x.ChatId == chatId && x.MessageId == messageId && x.Deleted, ct);

    private async Task SendMediaAsync(OutgoingAction action, Submission submission, SubmissionMedia[] media, bool work, CancellationToken ct)
    {
        IReadOnlyList<TelegramMessageReference> sent;
        if (work && !action.UseFileIds)
        {
            try { sent = await gateway.CopyPhotosAsync(submission.WorkChatId, submission.ArchiveChatId, media.Select(x => x.ArchiveMessageId!.Value).ToArray(), ct); }
            catch (ApiRequestException error) when (error.ErrorCode == 400)
            {
                action.UseFileIds = true;
                sent = await gateway.SendPhotosAsync(submission.WorkChatId, media.Select(x => new PhotoReference(x.FileId, x.CaptionSnapshot)).ToArray(), ct);
            }
        }
        else sent = await gateway.SendPhotosAsync(work ? submission.WorkChatId : submission.ArchiveChatId,
            media.Select(x => new PhotoReference(x.FileId, x.CaptionSnapshot)).ToArray(), ct);
        Record(submission.Id, sent, work);
        if (sent.Count != media.Length || sent.Any(x => x.ChatId != (work ? submission.WorkChatId : submission.ArchiveChatId)) || sent.Select(x => x.MessageId).Distinct().Count() != sent.Count)
        {
            action.UseFileIds = true;
            throw new IOException("Partial media batch."); // returned IDs remain recorded, completeness is not assumed
        }
        for (var i = 0; i < media.Length; i++)
        {
            if (work) media[i].WorkMessageId = sent[i].MessageId;
            else media[i].ArchiveMessageId = sent[i].MessageId;
        }
    }

    private async Task<int> SendCardAsync(Submission submission, bool work, CancellationToken ct)
    {
        var state = (await db.Games.SingleAsync(ct)).State;
        var allowButtons = work && state is GameState.Open or GameState.Review;
        var sent = await gateway.SendCardAsync(work ? submission.WorkChatId : submission.ArchiveChatId,
            submission.TextSnapshot.Length > 2800 ? $"Задание к ответу {submission.Id:N}:\n{submission.TextSnapshot}" : null,
            Card(submission, allowButtons), ct);
        Record(submission.Id, sent, work);
        if (sent.Count == 0) throw new IOException("Empty card delivery.");
        return sent[^1].MessageId;
    }

    private void Record(Guid submissionId, IReadOnlyList<TelegramMessageReference> sent, bool work)
    {
        foreach (var reference in sent.Distinct())
            db.DeliveryMessages.Add(new DeliveryMessage { SubmissionId = submissionId, ChatId = reference.ChatId, MessageId = reference.MessageId, IsWork = work });
    }

    private BotReply Card(Submission submission, bool allowButtons)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone);
        string Display(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm:ss zzz");
        var status = submission.Status switch
        {
            SubmissionState.Pending => "Ожидает проверки", SubmissionState.Approved => "Одобрено",
            SubmissionState.Rejected => $"Отклонено: {submission.RejectReason}", _ => "Не проверено"
        };
        var text = $"Ответ {submission.Id:N}\n{submission.AuthorSnapshot}\nБаллы: {submission.PointsSnapshot}\nОтправлен: {Display(submission.SubmittedAt)}\n" +
            (submission.TextSnapshot.Length <= 2800 ? $"Задание: {submission.TextSnapshot}\n" : "Полный текст задания — выше.\n") +
            $"Итог: {status}" + (submission.ModeratorId is { } actor ? $"\nОрганизатор: {actor}; решение: {(submission.DecidedAt is { } decided ? Display(decided) : "—")}" : "");
        return new(text, allowButtons && submission.Status == SubmissionState.Pending
            ? [[new("Approve", $"approve:{submission.Id:N}:{submission.Version}"), new("Reject", $"rejectask:{submission.Id:N}:{submission.Version}")]] : null);
    }

    private async Task<bool> CandidatePromptAsync(OutgoingAction action, CancellationToken ct)
    {
        var candidate = await db.AnswerCandidates.Include(x => x.Media).SingleAsync(x => x.Id == action.EntityId, ct);
        var participant = await db.Participants.SingleAsync(x => x.Id == candidate.ParticipantId, ct);
        if (candidate.State != CandidateState.Ready || candidate.Version != action.EntityVersion || participant.Disqualified ||
            (await db.Games.SingleAsync(ct)).State != GameState.Open || !await db.Attempts.AnyAsync(x => x.Id == candidate.AttemptId && x.State == AttemptState.Active, ct)) return true;
        var reply = new BotReply($"Подтвердить ответ ({candidate.Media.Count} фото)? Фото доступны организаторам и сохраняются в архиве.",
            [[new("Подтвердить", $"confirm:{candidate.Id:N}:{candidate.Version}"), new("Заменить", $"replace:{candidate.Id:N}:{candidate.Version}")]],
            candidate.Media.Min(x => x.SourceMessageId));
        var sent = await gateway.SendCardAsync(action.ChatId, null, reply, ct);
        if (sent.Count == 0) throw new IOException("Empty prompt delivery.");
        action.MessageId = sent[^1].MessageId;
        return true;
    }

    private async Task<bool> RejectPromptAsync(OutgoingAction action, CancellationToken ct)
    {
        var choice = await db.RejectChoices.SingleAsync(x => x.Id == action.EntityId, ct);
        var submission = await db.Submissions.SingleAsync(x => x.Id == choice.SubmissionId, ct);
        if (!choice.Active || submission.Status != SubmissionState.Pending || submission.Version != choice.SubmissionVersion ||
            (await db.Games.SingleAsync(ct)).State is not (GameState.Open or GameState.Review)) return true;
        var sent = await gateway.SendCardAsync(action.ChatId, null, JsonSerializer.Deserialize<BotReply>(action.Payload)!, ct);
        Record(submission.Id, sent, true);
        return true;
    }
}
