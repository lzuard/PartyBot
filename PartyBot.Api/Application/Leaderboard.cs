using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

public sealed record RankingRow(int ParticipantId, string Name, long Points, DateTimeOffset? LastSubmittedAt);

public sealed class Leaderboard(BotDbContext db)
{
    public async Task<IReadOnlyList<RankingRow>> ReadAsync(CancellationToken ct = default)
    {
        var participants = await db.Participants.AsNoTracking().Where(p => !p.Disqualified).ToListAsync(ct);
        var approved = await db.Submissions.AsNoTracking().Where(s => s.Status == SubmissionState.Approved).ToListAsync(ct);
        return participants.Select(p =>
        {
            var answers = approved.Where(s => s.ParticipantId == p.Id).ToArray();
            return new RankingRow(p.Id, p.DisplayName, answers.Sum(s => (long)s.PointsSnapshot),
                answers.Length == 0 ? null : answers.Max(s => s.SubmittedAt));
        }).OrderByDescending(r => r.Points).ThenBy(r => r.LastSubmittedAt).ThenBy(r => r.ParticipantId).ToArray();
    }

    public async Task<BotReply> ReplyAsync(Game game, long actor, CancellationToken ct)
    {
        if (game.State == GameState.Finished) return await FinalReplyAsync(game, actor, ct);
        var rows = await ReadAsync(ct);
        var text = "Рейтинг — топ-10\n" + string.Join("\n", rows.Take(10).Select((r, i) => $"{i + 1}. {r.Name} #{r.ParticipantId} — {r.Points}"));
        var own = await db.Participants.AsNoTracking().SingleOrDefaultAsync(p => p.TelegramUserId == actor, ct);
        if (own is null) text += "\nДля личного места зарегистрируйся: /start.";
        else if (own.Disqualified) text += "\nТы дисквалифицирован: места в рейтинге нет. История сохранена.";
        else
        {
            var index = rows.ToList().FindIndex(r => r.ParticipantId == own.Id);
            text += $"\nТвоё место: {index + 1}. {own.DisplayName} #{own.Id} — {rows[index].Points}";
        }
        return new(text, [[new("Обновить рейтинг", "rank")]]);
    }

    private async Task<BotReply> FinalReplyAsync(Game game, long actor, CancellationToken ct)
    {
        if (game.FinishedAt is null) return new("Игра завершена, но итоговый снимок отсутствует. Обратитесь к суперадмину.");
        var snapshot = await db.FinalStandings.AsNoTracking().Where(r => r.GameId == game.Id).ToListAsync(ct);
        var text = "Итоговый рейтинг — топ-10\n" + string.Join("\n", snapshot.Where(r => r.Rank != null).OrderBy(r => r.Rank).Take(10)
            .Select(r => $"{r.Rank}. {r.Name} #{r.ParticipantId} — {r.Points}"));
        var own = snapshot.SingleOrDefault(r => r.TelegramUserId == actor);
        text += own is null ? "\nТебя нет в итоговом снимке." : own.Disqualified
            ? $"\n{own.Name} #{own.ParticipantId}: DQ, места нет. Сохранённый результат: {own.Points}."
            : $"\nТвоё место: {own.Rank}. {own.Name} #{own.ParticipantId} — {own.Points}";
        if (game.ForceFinalized) text += $"\nФорс-финал. Непроверенных ответов: {game.UnreviewedCount}. Причина: {game.FinalReason}";
        return new(text, [[new("Итоговый рейтинг", "rank")]]);
    }
}
