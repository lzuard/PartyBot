using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

// Executed inside GameCommands' transaction and command gate. No Telegram calls here.
public sealed class PhotoCommands(BotDbContext db, TimeProvider time, BotOptions settings)
{
    public static bool IsModeration(CommandKind kind) => kind is CommandKind.Approve or CommandKind.AskReject or CommandKind.ConfirmReject or CommandKind.CancelReject;

    public async Task<BotReply> UnsupportedAsync(BotCommand command, Participant participant, TaskAvailability? active, CancellationToken ct)
    {
        if (active is not null && command.UnsupportedGroupId is { } group)
        {
            var candidate = await db.AnswerCandidates.SingleOrDefaultAsync(x => x.ParticipantId == participant.Id && x.MediaGroupId == group, ct);
            if (candidate is null)
                db.AnswerCandidates.Add(new AnswerCandidate { AttemptId = active.CurrentAttemptId!.Value, ParticipantId = participant.Id,
                    MediaGroupId = group, State = CandidateState.Cancelled, LastPartAt = time.GetUtcNow(), ReadyAt = time.GetUtcNow() });
            else if (candidate.State is CandidateState.Collecting or CandidateState.Ready)
            {
                candidate.State = CandidateState.Cancelled;
                candidate.Version++;
            }
        }
        return new("Ответ — одна фотография или альбом только из фотографий. Видео и документы не принимаются; пришли новый фотоответ.");
    }

    public async Task<BotReply?> ReceiveAsync(BotCommand command, Participant participant, TaskAvailability? active, CancellationToken ct)
    {
        if (active is null) return new("Сначала получи задание. Фото без активного задания не принимается.");
        if (command.Photo is not { MessageId: > 0 } photo || string.IsNullOrWhiteSpace(photo.FileId) || string.IsNullOrWhiteSpace(photo.FileUniqueId))
            return new("Нужна одна фотография или один фотоальбом Telegram.");
        if (await db.CandidateMedia.AnyAsync(x => x.SourceChatId == command.ChatId && x.SourceMessageId == photo.MessageId, ct))
            return null;
        var candidate = photo.MediaGroupId is null ? null : await db.AnswerCandidates.Include(x => x.Media)
            .SingleOrDefaultAsync(x => x.ParticipantId == participant.Id && x.MediaGroupId == photo.MediaGroupId, ct);
        if (candidate is not null && (candidate.AttemptId != active.CurrentAttemptId || candidate.State is not (CandidateState.Collecting or CandidateState.Ready)))
            return new("Эта часть альбома устарела. Подтверждённый или заменённый ответ нельзя дополнить.");
        if (candidate is null)
        {
            await InvalidateAsync(active.CurrentAttemptId!.Value, CandidateState.Replaced, ct);
            // Release the filtered unique index before adding the replacement, within this same transaction.
            await db.SaveChangesAsync(ct);
            candidate = new AnswerCandidate
            {
                AttemptId = active.CurrentAttemptId.Value, ParticipantId = participant.Id,
                MediaGroupId = photo.MediaGroupId
            };
            db.AnswerCandidates.Add(candidate);
        }
        else candidate.Version++;
        candidate.LastPartAt = time.GetUtcNow();
        candidate.ReadyAt = photo.MediaGroupId is null ? candidate.LastPartAt : candidate.LastPartAt.AddSeconds(settings.AlbumDebounceSeconds);
        candidate.State = photo.MediaGroupId is null ? CandidateState.Ready : CandidateState.Collecting;
        candidate.Media.Add(new CandidateMedia
        {
            SourceChatId = command.ChatId, SourceMessageId = photo.MessageId, FileId = photo.FileId,
            FileUniqueId = photo.FileUniqueId, Caption = photo.Caption
        });
        if (candidate.State == CandidateState.Ready) EnqueuePrompt(db, candidate, command.ChatId, time.GetUtcNow());
        return null;
    }

    public async Task<BotReply> CandidateAsync(BotCommand command, Participant participant, TaskAvailability? active, Game game, CancellationToken ct)
    {
        var candidate = await db.AnswerCandidates.Include(x => x.Media).SingleOrDefaultAsync(x => x.Id == command.EntityId, ct);
        if (candidate is null || candidate.ParticipantId != participant.Id || active is null || candidate.AttemptId != active.CurrentAttemptId ||
            candidate.Version != command.EntityVersion || candidate.State != CandidateState.Ready || candidate.ReadyAt > time.GetUtcNow())
            return new("Это подтверждение устарело или альбом ещё собирается.");
        if (command.Kind == CommandKind.ReplaceAnswer)
        {
            candidate.State = CandidateState.Replaced;
            candidate.Version++;
            return new("Пришли новое фото или один альбом. Задание осталось активным.");
        }
        var now = time.GetUtcNow();
        var submission = new Submission
        {
            AttemptId = candidate.AttemptId, ParticipantId = participant.Id, TaskId = active.TaskId,
            AuthorSnapshot = $"{participant.DisplayName} #{participant.Id}", TextSnapshot = active.Task.Text,
            PointsSnapshot = active.Task.Points, RepeatableSnapshot = active.Task.Repeatable, SubmittedAt = now,
            ArchiveChatId = settings.ArchiveChatId, WorkChatId = settings.ModerationChatId,
            Media = candidate.Media.OrderBy(x => x.SourceMessageId).Select((media, index) => new SubmissionMedia
            {
                Order = index, FileId = media.FileId, FileUniqueId = media.FileUniqueId,
                SourceChatId = media.SourceChatId, SourceMessageId = media.SourceMessageId, CaptionSnapshot = media.Caption
            }).ToList()
        };
        if (!PersonalQueue.MarkPending(participant, candidate.AttemptId, now)) throw new InvalidOperationException("Active attempt changed.");
        candidate.State = CandidateState.Confirmed;
        candidate.Version++;
        participant.NextTaskAvailableAt = now.AddMinutes(game.SubmitCooldownMinutes!.Value);
        participant.Version++;
        db.Submissions.Add(submission);
        Audit(command, submission, "SubmissionConfirmed");
        foreach (var kind in new[] { DeliveryKind.ArchiveMedia, DeliveryKind.ArchiveCard, DeliveryKind.WorkMedia, DeliveryKind.WorkCard })
            Enqueue(db, submission, kind, now);
        return new($"Ответ {submission.Id:N} принят на проверку. Следующее задание доступно после ожидания.", [[new("Получить задание", "get")]]);
    }

    public async Task InvalidateAsync(Guid attemptId, CandidateState state, CancellationToken ct)
    {
        foreach (var candidate in await db.AnswerCandidates.Where(x => x.AttemptId == attemptId &&
                     (x.State == CandidateState.Collecting || x.State == CandidateState.Ready)).ToListAsync(ct))
        {
            candidate.State = state;
            candidate.Version++;
        }
    }

    public async Task<BotReply?> ModerateAsync(BotCommand command, Game game, CancellationToken ct)
    {
        if (command.ActorId <= 0 || command.ChatId != settings.ModerationChatId || command.IsPrivate ||
            (command.ActorId != settings.SuperAdminUserId && !settings.OrganizerUserIds.Contains(command.ActorId)))
            return null; // Never post an unauthorized command's payload into the working board.
        if (game.State is not (GameState.Open or GameState.Review)) return new("Модерация закрыта.");
        RejectChoice? choice = null;
        if (command.Kind is CommandKind.ConfirmReject or CommandKind.CancelReject)
        {
            choice = await db.RejectChoices.SingleOrDefaultAsync(x => x.Id == command.EntityId, ct);
            if (choice is null || !choice.Active || choice.ActorId != command.ActorId) return new("Выбор причины устарел или принадлежит другому организатору.");
        }
        var id = choice?.SubmissionId ?? command.EntityId;
        var version = choice?.SubmissionVersion ?? command.EntityVersion;
        var submission = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (submission is null || submission.Status != SubmissionState.Pending || submission.Version != version || submission.WorkChatId != command.ChatId)
            return new("Заявка уже обработана или кнопка устарела.");
        if (command.Kind == CommandKind.CancelReject)
        {
            choice!.Active = false;
            return new("Выбор причины отменён. Заявка остаётся на проверке.");
        }
        if (command.Kind == CommandKind.AskReject)
        {
            choice = new RejectChoice { ActorId = command.ActorId, SubmissionId = submission.Id, SubmissionVersion = submission.Version };
            db.RejectChoices.Add(choice);
            var buttons = settings.RejectReasons.Select(reason => new[] { new BotButton(reason.Value, $"reject:{choice.Id:N}:{reason.Key}") })
                .Append([new BotButton("Отмена", $"rejectcancel:{choice.Id:N}")]).ToArray();
            db.OutgoingActions.Add(new OutgoingAction
            {
                LogicalKey = $"reject-choice:{choice.Id}", Kind = DeliveryKind.RejectPrompt, EntityId = choice.Id,
                ChatId = submission.WorkChatId, RetryAt = time.GetUtcNow(),
                Payload = JsonSerializer.Serialize(new BotReply($"Причина отклонения {submission.Id:N}:", buttons))
            });
            return null;
        }
        string? reasonText = null;
        if (command.Kind == CommandKind.ConfirmReject && (command.ReasonId is null || !settings.RejectReasons.TryGetValue(command.ReasonId, out reasonText)))
            return new("Выбери причину из списка.");
        var approved = command.Kind == CommandKind.Approve;
        var participant = await db.Participants.Include(x => x.Tasks).ThenInclude(x => x.Task)
            .Include(x => x.Tasks).ThenInclude(x => x.CurrentAttempt).SingleAsync(x => x.Id == submission.ParticipantId, ct);
        if (!PersonalQueue.ResolvePending(participant, submission.AttemptId, approved)) throw new InvalidOperationException("Pending availability missing.");
        participant.Version++;
        submission.Status = approved ? SubmissionState.Approved : SubmissionState.Rejected;
        submission.Version++;
        submission.ModeratorId = command.ActorId;
        submission.DecidedAt = time.GetUtcNow();
        submission.RejectReason = reasonText;
        if (choice is not null) choice.Active = false;
        if (approved)
        {
            db.ScoreEntries.Add(new ScoreEntry { SubmissionId = submission.Id, ParticipantId = participant.Id, Points = submission.PointsSnapshot, CreatedAt = submission.DecidedAt.Value });
            Audit(command, submission, "ScoreAwarded");
        }
        Audit(command, submission, approved ? "SubmissionApproved" : "SubmissionRejected", reasonText);
        if (!approved || submission.RepeatableSnapshot)
            Audit(command, submission, approved ? "TaskReturnedToPool" : "TaskReturnedToQueue");
        foreach (var kind in new[] { DeliveryKind.RefreshArchive, DeliveryKind.RefreshWork, DeliveryKind.CleanupWork })
            Enqueue(db, submission, kind, time.GetUtcNow());
        db.OutgoingActions.Add(new OutgoingAction
        {
            LogicalKey = $"submission:{submission.Id}:result", ChatId = participant.PrivateChatId, RetryAt = time.GetUtcNow(),
            Payload = JsonSerializer.Serialize(new BotReply(approved
                ? $"Ответ {submission.Id:N} одобрен. Начислено баллов: {submission.PointsSnapshot}."
                : $"Ответ {submission.Id:N} отклонён: {reasonText}. Задание возвращено в очередь."))
        });
        // The working card is updated by delivery; no redundant decision message to clean up.
        return null;
    }

    private void Audit(BotCommand command, Submission submission, string action, string? data = null) => db.AuditEvents.Add(new AuditEvent
    {
        At = time.GetUtcNow(), ActorId = command.ActorId, ParticipantId = submission.ParticipantId,
        TaskId = submission.TaskId, AttemptId = submission.AttemptId, SubmissionId = submission.Id, Action = action, Data = data
    });

    public static void Enqueue(BotDbContext db, Submission submission, DeliveryKind kind, DateTimeOffset now) => db.OutgoingActions.Add(new OutgoingAction
    {
        LogicalKey = $"submission:{submission.Id}:{kind}", Kind = kind, EntityId = submission.Id, RetryAt = now,
        ChatId = kind is DeliveryKind.ArchiveMedia or DeliveryKind.ArchiveCard or DeliveryKind.RefreshArchive ? submission.ArchiveChatId : submission.WorkChatId
    });
    public static void EnqueuePrompt(BotDbContext db, AnswerCandidate candidate, long chatId, DateTimeOffset now) => db.OutgoingActions.Add(new OutgoingAction
    {
        LogicalKey = $"candidate:{candidate.Id}:{candidate.Version}", Kind = DeliveryKind.CandidatePrompt,
        EntityId = candidate.Id, EntityVersion = candidate.Version, ChatId = chatId, RetryAt = now
    });
}
