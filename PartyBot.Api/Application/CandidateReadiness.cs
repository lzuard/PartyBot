using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

public sealed class CandidateReadiness(BotDbContext db, CommandGate gate, TimeProvider time)
{
    public async Task ProcessAsync(CancellationToken ct = default)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            if ((await db.Games.SingleAsync(ct)).State != GameState.Open) return;
            var now = time.GetUtcNow();
            var candidates = await db.AnswerCandidates.Where(x => x.State == CandidateState.Collecting && x.ReadyAt <= now).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                var participant = await db.Participants.SingleAsync(x => x.Id == candidate.ParticipantId, ct);
                if (participant.Disqualified || !await db.Attempts.AnyAsync(x => x.Id == candidate.AttemptId && x.State == AttemptState.Active, ct)) continue;
                candidate.State = CandidateState.Ready;
                PhotoCommands.EnqueuePrompt(db, candidate, participant.PrivateChatId, now);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }
}
