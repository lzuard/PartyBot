using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

// Operational recovery changes delivery state only, including after Finished.
public sealed class OperationsCommands(BotDbContext db, TimeProvider time, BotOptions options)
{
    public static bool Handles(CommandKind kind) => kind is CommandKind.Diagnostics or CommandKind.AskRetryDelivery or
        CommandKind.ConfirmRetryDelivery or CommandKind.CancelRetryDelivery;

    public async Task<BotReply> ApplyAsync(BotCommand command, Game game, CancellationToken ct)
    {
        if (!command.IsPrivate || command.ActorId != options.SuperAdminUserId || command.ChatId != options.SuperAdminUserId)
            return new("Диагностика доступна только суперадмину в личном чате.");
        if (command.Kind == CommandKind.Diagnostics) return await DiagnosticsAsync(command.Page, game, ct);
        RecoveryRequest? request = null;
        long actionId;
        if (command.Kind == CommandKind.AskRetryDelivery)
        {
            if (!long.TryParse(command.Name, out actionId) || actionId <= 0) return new("Укажите ID доставки: /retry НОМЕР.");
        }
        else
        {
            request = await db.RecoveryRequests.SingleOrDefaultAsync(r => r.Id == command.EntityId, ct);
            if (request is null || !request.Active || request.ActorId != command.ActorId) return new("Подтверждение повтора уже неактуально.");
            if (command.Kind == CommandKind.CancelRetryDelivery)
            {
                request.Active = false;
                return new("Повтор отменён.");
            }
            actionId = request.ActionId;
        }
        var action = await db.OutgoingActions.SingleOrDefaultAsync(a => a.Id == actionId, ct);
        if (action is null || !action.Failed || action.Done || action.Kind == DeliveryKind.Text && action.MessageId != null ||
            request is not null && request.Attempts != action.Attempts)
            return new("Доставка не требует ручного повтора или уже изменилась. Обновите /diagnostics.");
        if (request is null)
        {
            request = new RecoveryRequest { ActorId = command.ActorId, ActionId = action.Id, Attempts = action.Attempts };
            db.RecoveryRequests.Add(request);
            return new($"Повторить доставку #{action.Id} ({action.Kind})? Сначала исправьте права/настройки. После неопределённого сетевого сбоя возможен дубль сообщения. Решение и баллы не меняются.",
                [[new("Повторить", $"retryyes:{request.Id:N}"), new("Отмена", $"retryno:{request.Id:N}")]]);
        }
        request.Active = false;
        action.Failed = false;
        action.RetryAt = time.GetUtcNow();
        // WorkCard may have settled without a card when its media prerequisite permanently failed.
        if (action.Kind == DeliveryKind.WorkMedia)
        {
            var submission = await db.Submissions.SingleAsync(s => s.Id == action.EntityId, ct);
            if (submission.Status == SubmissionState.Pending && submission.WorkCardId is null)
            {
                var card = await db.OutgoingActions.SingleAsync(a => a.EntityId == submission.Id && a.Kind == DeliveryKind.WorkCard, ct);
                if (card.Done) { card.Done = false; card.RetryAt = time.GetUtcNow(); }
            }
        }
        // Keep Attempts, errors, media refs, partial batches and deleted-message flags as recovery evidence.
        db.AuditEvents.Add(new AuditEvent { At = time.GetUtcNow(), ActorId = command.ActorId,
            Action = "DeliveryRetryRequested", Data = $"ActionId={action.Id}; Kind={action.Kind}; Attempts={action.Attempts}" });
        return new($"Доставка #{action.Id} поставлена на повтор.", [[new("Диагностика", "diagnostics:0")]]);
    }

    private async Task<BotReply> DiagnosticsAsync(int requestedPage, Game game, CancellationToken ct)
    {
        var outstanding = db.OutgoingActions.Where(a => a.Kind == DeliveryKind.Text ? a.MessageId == null : !a.Done);
        var failed = await outstanding.CountAsync(a => a.Failed, ct);
        var errors = outstanding.Where(a => a.Failed || a.Attempts > 0);
        var errorCount = await errors.CountAsync(ct);
        var page = Math.Clamp(requestedPage, 0, Math.Max(0, (errorCount - 1) / 8));
        var items = await errors.OrderByDescending(a => a.Failed).ThenBy(a => a.Id).Skip(page * 8).Take(8).ToListAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.DisplayTimeZone);
        var text = $"Диагностика. Игра: {game.State}\n" +
            $"Pending: {await db.Submissions.CountAsync(s => s.Status == SubmissionState.Pending, ct)}\n" +
            $"Неполный архив: {await db.Submissions.CountAsync(s => s.ArchiveCardId == null || s.Media.Any(m => m.ArchiveMessageId == null), ct)}\n" +
            $"Сборка альбомов: {await db.AnswerCandidates.CountAsync(c => c.State == CandidateState.Collecting, ct)}\n" +
            $"Доставки: {await outstanding.CountAsync(ct)}; Failed: {failed}; с ошибкой доски/cleanup: {await outstanding.CountAsync(a => a.Attempts > 0 && (a.Kind == DeliveryKind.WorkMedia || a.Kind == DeliveryKind.WorkCard || a.Kind == DeliveryKind.RefreshWork || a.Kind == DeliveryKind.CleanupWork), ct)}\n" +
            $"Следующий offset: {(await db.PollingStates.SingleAsync(ct)).NextUpdateId?.ToString() ?? "не задан"}\n" +
            $"Ошибки, страница {page + 1}:";
        var buttons = new List<BotButton[]>();
        foreach (var item in items)
        {
            text += $"\n#{item.Id} {item.Kind}; {(item.Failed ? "Failed" : "ожидает повтора")}; попыток {item.Attempts}; {item.LastError ?? "ошибка"}; retry {TimeZoneInfo.ConvertTime(item.RetryAt, zone):HH:mm:ss zzz}";
            if (item.Failed) buttons.Add([new($"Повтор #{item.Id}", $"retry:{item.Id}")]);
        }
        if (page > 0) buttons.Add([new("Назад", $"diagnostics:{page - 1}")]);
        if ((page + 1) * 8 < errorCount) buttons.Add([new("Далее", $"diagnostics:{page + 1}")]);
        buttons.Add([new("Обновить", $"diagnostics:{page}"), new("Панель", "admin:0")]);
        text += "\nВременные ошибки повторяются автоматически. Failed: /retry НОМЕР после исправления причины. Ожидающие зависимости не сбрасывайте.";
        return new(text, buttons.ToArray());
    }
}
