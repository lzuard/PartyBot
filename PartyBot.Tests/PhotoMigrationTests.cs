using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

public sealed class PhotoMigrationTests
{
    [Fact]
    public async Task Upgrade_from_stage_two_preserves_active_queue_and_old_outgoing_replies()
    {
        await using var f = new GameFixture();
        Guid attemptId;
        string[] queue;
        await using (var db = f.Open())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261018183844_GameQueueRegistration");
            db.Games.Add(new Game { State = GameState.Open, SkipCooldownMinutes = 1, SubmitCooldownMinutes = 1 });
            var tasks = QueueTests.Catalog();
            db.TaskDefinitions.AddRange(tasks);
            var participant = new Participant { TelegramUserId = 100, PrivateChatId = 100, DisplayName = "Old" };
            PersonalQueue.Initialize(participant, tasks, new SeededRandomSource(42));
            db.Participants.Add(participant);
            await db.SaveChangesAsync();
            var attempt = PersonalQueue.Take(participant, f.Clock.GetUtcNow())!;
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptId = attempt.Id;
            queue = PersonalQueue.Queued(participant).Select(x => x.TaskId).ToArray();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO OutgoingActions (LogicalKey, ChatId, Payload, RetryAt, Attempts, Failed)
                VALUES ('old-reply', 100, {0}, 0, 0, 0)
                """, "{\"Text\":\"old\"}");
        }
        await using (var db = f.Open())
        {
            await new DatabaseBootstrap(db, f.Clock).InitializeAsync();
            Assert.Equal(AttemptState.Active, (await db.Attempts.FindAsync(attemptId))!.State);
            var participant = await db.Participants.Include(x => x.Tasks).ThenInclude(x => x.Task).SingleAsync();
            Assert.Equal(queue, PersonalQueue.Queued(participant).Select(x => x.TaskId));
            Assert.Equal(DeliveryKind.Text, (await db.OutgoingActions.SingleAsync()).Kind);
            Assert.Empty(await db.Submissions.ToListAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }
}
