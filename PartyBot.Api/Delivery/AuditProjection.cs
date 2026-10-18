using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Configuration;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Delivery;

// Replayable projection: audit remains append-only; unique outbox keys record publication intent.
public sealed class AuditProjection(BotDbContext db, CommandGate gate, TimeProvider time, IOptions<BotOptions> options)
{
    public async Task<int> EnqueueAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        if (settings.AuditChatId == 0) return 0;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var events = await db.AuditEvents.AsNoTracking()
                .Where(e => !db.OutgoingActions.Any(a => a.EntityId == e.Id && a.LogicalKey.StartsWith("audit:")))
                .OrderBy(e => e.At).ThenBy(e => e.Id).Take(100).ToListAsync(ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.DisplayTimeZone);
            foreach (var e in events)
            {
                var text = $"Событие {e.Id:N}\n{TimeZoneInfo.ConvertTime(e.At, zone):yyyy-MM-dd HH:mm:ss zzz}\n" +
                    $"Actor: {e.ActorId?.ToString() ?? "system"}; действие: {e.Action}\n" +
                    $"Participant: {e.ParticipantId}; Task: {e.TaskId}; Attempt: {e.AttemptId:N}; Submission: {e.SubmissionId:N}" +
                    (e.Data is null ? "" : $"\nДанные/причина: {e.Data}");
                db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"audit:{e.Id:N}", EntityId = e.Id,
                    ChatId = settings.AuditChatId, RetryAt = time.GetUtcNow(),
                    Payload = JsonSerializer.Serialize(new BotReply(text, MessageThreadId: settings.AuditThreadId)) });
            }
            if (settings.SuperAdminUserId > 0)
            {
                var failures = await db.OutgoingActions.Where(a => a.LogicalKey.StartsWith("audit:") && a.Attempts > 0 && a.MessageId == null).ToListAsync(ct);
                foreach (var failure in failures)
                {
                    var key = $"audit-alert:{failure.Id}:{failure.Failed}";
                    if (await db.OutgoingActions.AnyAsync(a => a.LogicalKey == key, ct)) continue;
                    db.OutgoingActions.Add(new OutgoingAction { LogicalKey = key, ChatId = settings.SuperAdminUserId,
                        RetryAt = time.GetUtcNow(), Payload = JsonSerializer.Serialize(new BotReply(
                            $"Ошибка бизнес-журнала, доставка #{failure.Id}: {failure.LastError}. " +
                            (failure.Failed ? "Нужна проверка прав/настроек." : "Запланирован повтор."))) });
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return events.Count;
        }
        finally { gate.Semaphore.Release(); }
    }
}
