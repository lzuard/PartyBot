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
        // Stage 5 supplies the immutable final snapshot; never substitute a live ranking for it.
        if (game.State == GameState.Finished) return new("Игра завершена. Итоговый снимок пока недоступен.");
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
}
