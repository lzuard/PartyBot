using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

public sealed class AdminCommands(BotDbContext db, TimeProvider time, BotOptions options)
{
    public static bool Handles(CommandKind kind) => kind is CommandKind.Admin or CommandKind.AdminParticipant or
        CommandKind.AskDisqualify or CommandKind.AskReinstate or CommandKind.ConfirmDiscipline or CommandKind.CancelDiscipline;

    public async Task<BotReply> ApplyAsync(BotCommand command, Game game, CancellationToken ct)
    {
        if (!command.IsPrivate || command.ActorId != options.SuperAdminUserId || command.ChatId != options.SuperAdminUserId)
            return new("Панель доступна только суперадмину в личном чате.");
        if (command.Kind == CommandKind.Admin)
        {
            var people = await db.Participants.OrderBy(p => p.Id).ToListAsync(ct);
            var page = Math.Max(0, command.Page);
            page = Math.Min(page, Math.Max(0, (people.Count - 1) / 10));
            var buttons = people.Skip(page * 10).Take(10).Select(p => new[] { new BotButton($"#{p.Id} {p.DisplayName}{(p.Disqualified ? " [DQ]" : "")}", $"person:{p.Id}") }).ToList();
            if (page > 0) buttons.Add([new("Назад", $"admin:{page - 1}")]);
            if ((page + 1) * 10 < people.Count) buttons.Add([new("Далее", $"admin:{page + 1}")]);
            buttons.Add([new("Рейтинг", "rank")]);
            if (game.State == GameState.Registration) buttons.Add([new("Открыть игру", "open")]);
            return new($"Панель суперадмина. Игра: {game.State}. Участников: {people.Count}.\nВыберите участника (страница {page + 1}).", buttons.ToArray());
        }
        if (game.State == GameState.Finished && command.Kind != CommandKind.AdminParticipant)
            return new("После финала дисквалификация и восстановление запрещены.");
        if (command.Kind is CommandKind.ConfirmDiscipline or CommandKind.CancelDiscipline)
        {
            var request = await db.DisciplineRequests.SingleOrDefaultAsync(r => r.Id == command.EntityId, ct);
            if (request is null || !request.Active || request.ActorId != command.ActorId || request.GameVersion != game.Version)
                return new("Это подтверждение уже неактуально.");
            var person = await db.Participants.SingleAsync(p => p.Id == request.ParticipantId, ct);
            if (person.Version != request.ParticipantVersion || person.Disqualified == request.Disqualify)
                return new("Участник изменился. Откройте его карточку заново.");
            request.Active = false;
            if (command.Kind == CommandKind.CancelDiscipline) return new("Действие отменено.");
            person.Disqualified = request.Disqualify;
            person.Version++;
            db.AuditEvents.Add(new AuditEvent { At = time.GetUtcNow(), ActorId = command.ActorId, ParticipantId = person.Id,
                Action = request.Disqualify ? "Disqualified" : "Reinstated", Data = request.Reason });
            return new($"{person.DisplayName} #{person.Id}: {(person.Disqualified ? "дисквалифицирован" : "восстановлен")}. Причина: {request.Reason}", [[new("Панель", "admin:0")]]);
        }
        var targetId = command.ParticipantId;
        string? reason = null;
        if (command.Kind is CommandKind.AskDisqualify or CommandKind.AskReinstate)
        {
            var parts = (command.Name ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0].TrimStart('#'), out var parsed) || parsed <= 0)
                return new("Укажите номер и причину: /dq НОМЕР ПРИЧИНА или /reinstate НОМЕР ПРИЧИНА.");
            targetId = parsed;
            reason = parts[1].Trim();
            if (reason.Length is < 1 or > 300 || reason.Any(char.IsControl)) return new("Причина: 1–300 символов без управляющих знаков.");
        }
        var target = await db.Participants.SingleOrDefaultAsync(p => p.Id == targetId, ct);
        if (target is null) return new("Участник не найден.");
        if (command.Kind == CommandKind.AdminParticipant)
            return new($"{target.DisplayName} #{target.Id}. {(target.Disqualified ? "DQ" : "Допущен")}\n" +
                (game.State == GameState.Finished ? "Итог закрыт для изменений." : $"Для {(target.Disqualified ? "восстановления" : "дисквалификации")}: /{(target.Disqualified ? "reinstate" : "dq")} {target.Id} ПРИЧИНА"), [[new("Панель", "admin:0")]]);
        var disqualify = command.Kind == CommandKind.AskDisqualify;
        if (target.Disqualified == disqualify) return new("Участник уже в этом состоянии.");
        var confirmation = new DisciplineRequest { ActorId = command.ActorId, ParticipantId = target.Id, ParticipantVersion = target.Version,
            GameVersion = game.Version, Disqualify = disqualify, Reason = reason! };
        db.DisciplineRequests.Add(confirmation);
        return new($"{(disqualify ? "Дисквалифицировать" : "Восстановить")} {target.DisplayName} #{target.Id}?\nПричина: {reason}\nИстория, очередь и кулдаун сохраняются.",
            [[new("Да", $"disciplineyes:{confirmation.Id:N}"), new("Нет", $"disciplineno:{confirmation.Id:N}")]]);
    }
}
