using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using static PartyBot.Tests.FinalizationTests;

namespace PartyBot.Tests;

public sealed class FinalSnapshotTests
{
    [Fact]
    public async Task Snapshot_preserves_full_order_and_personal_place_beyond_top_ten()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        await f.OpenGameAsync();
        for (var i = 0; i < 13; i++) await f.RunAsync(CommandKind.Name, 100 + i, $"player{i}");
        await using (var db = f.Open())
        {
            var people = await db.Participants.OrderBy(p => p.Id).ToListAsync();
            var task = await db.TaskDefinitions.FirstAsync();
            for (var i = 0; i < 3; i++)
            {
                var attempt = new Attempt { ParticipantId = people[i].Id, TaskId = task.Id, State = AttemptState.Submitted, TakenAt = f.Clock.GetUtcNow() };
                db.Attempts.Add(attempt);
                db.Submissions.Add(new Submission { AttemptId = attempt.Id, ParticipantId = people[i].Id, TaskId = task.Id,
                    PointsSnapshot = 2, Status = SubmissionState.Approved,
                    SubmittedAt = f.Clock.GetUtcNow().AddMinutes(i == 0 ? 1 : 0), DecidedAt = f.Clock.GetUtcNow().AddMinutes(5 - i) });
            }
            people[3].Disqualified = true;
            await db.SaveChangesAsync();
        }
        await Close(f);
        await Confirm(f, await Ask(f, CommandKind.AskFinalize));
        await using (var db = f.Open())
        {
            var live = await new Leaderboard(db).ReadAsync();
            var snapshot = await db.FinalStandings.Where(r => r.Rank != null).OrderBy(r => r.Rank).ToListAsync();
            Assert.Equal(live.Select(r => (r.ParticipantId, r.Points, r.LastSubmittedAt)), snapshot.Select(r => (r.ParticipantId, r.Points, r.LastApprovedSubmittedAt)));
            Assert.Equal(new long[] { 101, 102, 100 }, snapshot.Take(3).Select(r => r.TelegramUserId));
            Assert.All(snapshot.Skip(3), r => Assert.Null(r.LastApprovedSubmittedAt));
        }
        var reply = (await f.RunAsync(CommandKind.Leaderboard, 112))!.Text;
        Assert.Contains("Твоё место: 12.", reply);
        Assert.DoesNotContain("11. player", reply);
    }

    [Fact]
    public async Task Empty_final_is_valid_and_cancelled_force_cannot_be_reused()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        Assert.Null((await f.RunAsync(CommandKind.AskFinalize, 999))!.Buttons);
        Assert.Null((await f.RunAsync(CommandKind.AskClose, 999))!.Buttons);
        await f.OpenGameAsync();
        await Close(f);
        var force = await Ask(f, CommandKind.AskForceFinalize, "empty");
        await f.ExecuteAsync(f.Command(CommandKind.CancelTransition, 999) with { EntityId = force });
        await Confirm(f, force);
        await using (var db = f.Open()) Assert.Equal(GameState.Review, (await db.Games.SingleAsync()).State);
        await Confirm(f, await Ask(f, CommandKind.AskFinalize));
        var result = await f.RunAsync(CommandKind.Leaderboard);
        Assert.Contains("нет в итоговом снимке", result!.Text);
        await using var check = f.Open();
        Assert.NotNull((await check.Games.SingleAsync()).FinishedAt);
        Assert.Empty(await check.FinalStandings.ToListAsync());
    }

    [Fact]
    public async Task Upgrade_stage_four_keeps_existing_game_and_adds_empty_final_storage()
    {
        await using var f = new GameFixture();
        await using (var db = f.Open())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261018231449_RankingPanelsAudit");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Games (Id, State, Version, DemonstrationCatalog, SkipCooldownMinutes, SubmitCooldownMinutes) VALUES (1, 1, 7, 0, 2, 3)");
        }
        await using var actual = f.Open();
        await new DatabaseBootstrap(actual, f.Clock).InitializeAsync();
        var game = await actual.Games.SingleAsync();
        Assert.Equal(GameState.Open, game.State);
        Assert.Equal(7, game.Version);
        Assert.Equal(3, game.SubmitCooldownMinutes);
        Assert.Null(game.FinishedAt);
        Assert.Null(game.ClosedAt);
        Assert.False(game.ForceFinalized);
        Assert.Empty(await actual.FinalStandings.ToListAsync());
        Assert.Empty(await actual.TransitionRequests.ToListAsync());
        Assert.False(actual.Database.HasPendingModelChanges());
    }
}
