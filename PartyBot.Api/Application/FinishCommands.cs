using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

// Called only inside GameCommands' shared gate and transaction, just like Confirm/Approve.
public sealed class FinishCommands(BotDbContext db, TimeProvider time, BotOptions options)
{
    public static bool Handles(CommandKind kind) => kind is CommandKind.AskClose or CommandKind.AskFinalize or
        CommandKind.AskForceFinalize or CommandKind.ConfirmTransition or CommandKind.CancelTransition;

    public async Task<BotReply> ApplyAsync(BotCommand command, Game game, CancellationToken ct)
    {
        if (!command.IsPrivate || command.ActorId != options.SuperAdminUserId || command.ChatId != options.SuperAdminUserId)
            return new("Переходы игры доступны только суперадмину в личном чате.");
        TransitionRequest? request = null;
        if (command.Kind is CommandKind.ConfirmTransition or CommandKind.CancelTransition)
        {
            request = await db.TransitionRequests.SingleOrDefaultAsync(r => r.Id == command.EntityId, ct);
            if (request is null || !request.Active || request.ActorId != command.ActorId || request.GameVersion != game.Version)
                return new("Это подтверждение уже неактуально.");
        }
        var kind = request?.Kind ?? (command.Kind switch
        {
            CommandKind.AskClose => TransitionKind.CloseIntake,
            CommandKind.AskFinalize => TransitionKind.Finalize,
            _ => TransitionKind.ForceFinalize
        });
        var expectedState = kind == TransitionKind.CloseIntake ? GameState.Open : GameState.Review;
        if (game.State != expectedState) return new("Действие недоступно в текущем состоянии игры.");
        if (command.Kind == CommandKind.CancelTransition)
        {
            request!.Active = false;
            return new("Переход отменён.", [[new("Панель", "admin:0")]]);
        }
        var pending = await db.Submissions.Where(s => s.Status == SubmissionState.Pending).ToListAsync(ct);
        if (kind == TransitionKind.Finalize && pending.Count > 0)
            return new($"Финал запрещён: Pending — {pending.Count}. Завершите проверку либо используйте отдельный форс-финал: /forcefinalize ПРИЧИНА.");
        if (request is null)
        {
            var reason = kind == TransitionKind.ForceFinalize ? command.Name?.Trim() : null;
            if (kind == TransitionKind.ForceFinalize && (string.IsNullOrWhiteSpace(reason) || reason.Length > 300 || reason.Any(char.IsControl)))
                return new("Форс-финал оставит непроверенные ответы без баллов. Укажите причину (1–300 символов): /forcefinalize ПРИЧИНА.");
            request = new TransitionRequest { ActorId = command.ActorId, GameVersion = game.Version, Kind = kind,
                PendingCount = pending.Count, Reason = reason };
            db.TransitionRequests.Add(request);
            var text = kind switch
            {
                TransitionKind.CloseIntake => $"Закрыть приём? Выдача, пропуск, регистрация и подтверждение фото будут закрыты. Модерация продолжится. Pending: {pending.Count}.",
                TransitionKind.Finalize => "Завершить игру? Pending: 0. Рейтинг будет зафиксирован; изменения и модерация закроются.",
                _ => $"ФОРС-ФИНАЛ. Pending: {pending.Count}. Эти ответы станут Unreviewed без баллов и возврата заданий. Это может изменить победителя.\nПричина: {reason}\nЗафиксировать итог без возможности изменений?"
            };
            return new(text, [[new(kind == TransitionKind.ForceFinalize ? "Подтвердить форс-финал" : "Да", $"transitionyes:{request.Id:N}"), new("Нет", $"transitionno:{request.Id:N}")]]);
        }
        if (kind == TransitionKind.ForceFinalize && request.PendingCount != pending.Count)
        {
            request.Active = false;
            return new($"Число Pending изменилось: было {request.PendingCount}, сейчас {pending.Count}. Повторите /forcefinalize ПРИЧИНА и подтвердите актуальное число.");
        }
        request.Active = false;
        var now = time.GetUtcNow();
        game.Version++;
        if (kind == TransitionKind.CloseIntake)
        {
            game.State = GameState.Review;
            game.ClosedAt = now;
            db.AuditEvents.Add(new AuditEvent { At = now, ActorId = command.ActorId, Action = "IntakeClosed" });
            return new("Приём закрыт. Игра в Review; Pending можно проверить.", [[new("Панель", "admin:0")]]);
        }
        foreach (var submission in pending)
        {
            submission.Status = SubmissionState.Unreviewed;
            submission.Version++;
            submission.ModeratorId = command.ActorId;
            submission.DecidedAt = now;
            var availability = await db.TaskAvailabilities.SingleAsync(a => a.ParticipantId == submission.ParticipantId && a.TaskId == submission.TaskId, ct);
            availability.Location = TaskLocation.Consumed;
            availability.CurrentAttemptId = null;
            db.AuditEvents.Add(new AuditEvent { At = now, ActorId = command.ActorId, ParticipantId = submission.ParticipantId,
                TaskId = submission.TaskId, AttemptId = submission.AttemptId, SubmissionId = submission.Id,
                Action = "SubmissionUnreviewed", Data = request.Reason });
            foreach (var delivery in new[] { DeliveryKind.RefreshArchive, DeliveryKind.RefreshWork, DeliveryKind.CleanupWork })
                PhotoCommands.Enqueue(db, submission, delivery, now);
            var person = await db.Participants.SingleAsync(p => p.Id == submission.ParticipantId, ct);
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"submission:{submission.Id}:result", ChatId = person.PrivateChatId, RetryAt = now,
                Payload = JsonSerializer.Serialize(new BotReply($"Ответ {submission.Id:N} остался непроверенным (Unreviewed): форс-финал. Баллы не начислены. Причина: {request.Reason}")) });
        }
        var participants = await db.Participants.ToListAsync(ct);
        var approved = await db.Submissions.Where(s => s.Status == SubmissionState.Approved).ToListAsync(ct);
        var standings = participants.Select(p =>
        {
            var answers = approved.Where(s => s.ParticipantId == p.Id).ToArray();
            return new FinalStanding { ParticipantId = p.Id, TelegramUserId = p.TelegramUserId, Name = p.DisplayName,
                Disqualified = p.Disqualified, Points = answers.Sum(s => (long)s.PointsSnapshot),
                LastApprovedSubmittedAt = answers.Length == 0 ? null : answers.Max(s => s.SubmittedAt) };
        }).ToArray();
        var rank = 0;
        foreach (var row in standings.Where(r => !r.Disqualified).OrderByDescending(r => r.Points).ThenBy(r => r.LastApprovedSubmittedAt).ThenBy(r => r.ParticipantId))
            row.Rank = ++rank;
        db.FinalStandings.AddRange(standings);
        game.State = GameState.Finished;
        game.FinishedAt = now;
        game.FinalizedBy = command.ActorId;
        game.ForceFinalized = kind == TransitionKind.ForceFinalize;
        game.FinalReason = request.Reason;
        game.UnreviewedCount = pending.Count;
        db.AuditEvents.Add(new AuditEvent { At = now, ActorId = command.ActorId, Action = game.ForceFinalized ? "GameForceFinalized" : "GameFinalized",
            Data = JsonSerializer.Serialize(new { Forced = game.ForceFinalized, Reason = request.Reason, UnreviewedCount = pending.Count }) });
        return new("Игра завершена. Итоговый рейтинг сохранён.", [[new("Итоговый рейтинг", "rank")]]);
    }
}
