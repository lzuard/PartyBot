using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

public sealed class FinalizationTests
{
    internal static async Task<Guid> Ask(GameFixture f, CommandKind kind, string? reason = null)
    {
        var reply = await f.RunAsync(kind, 999, reason);
        var button = Assert.Single(reply!.Buttons![0], b => b.Data.StartsWith("transitionyes:"));
        return Guid.ParseExact(button.Data.Split(':')[1], "N");
    }
    internal static Task<BotReply?> Confirm(GameFixture f, Guid id) => f.ExecuteAsync(f.Command(CommandKind.ConfirmTransition, 999) with { EntityId = id });
    internal static async Task Close(GameFixture f) => await Confirm(f, await Ask(f, CommandKind.AskClose));

    [Fact]
    public async Task Close_preserves_active_candidate_and_blocks_all_intake_but_keeps_moderation()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var pending = await PhotoTestSupport.SubmitAsync(f);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.RunAsync(CommandKind.GetTask);
        await PhotoTestSupport.PhotoAsync(f, 200);
        var candidate = await PhotoTestSupport.CandidateAsync(f);
        var before = await f.ParticipantAsync();
        await Close(f);
        await f.RunAsync(CommandKind.GetTask);
        await f.RunAsync(CommandKind.ConfirmSkip, attempt: candidate.AttemptId);
        await PhotoTestSupport.ClickAsync(f, candidate);
        await PhotoTestSupport.PhotoAsync(f, 201);
        await f.RunAsync(CommandKind.Name, 101, "late");
        await f.RunAsync(CommandKind.Name, name: "rename");
        var after = await f.ParticipantAsync();
        Assert.Equal(before.DisplayName, after.DisplayName);
        Assert.Equal(before.NextTaskAvailableAt, after.NextTaskAvailableAt);
        Assert.Equal(before.NextBucket, after.NextBucket);
        Assert.Equal(before.Tasks.OrderBy(t => t.TaskId).Select(t => (t.Location, t.Position, t.CurrentAttemptId)),
            after.Tasks.OrderBy(t => t.TaskId).Select(t => (t.Location, t.Position, t.CurrentAttemptId)));
        await using (var db = f.Open())
        {
            Assert.Equal(GameState.Review, (await db.Games.SingleAsync()).State);
            Assert.Equal(f.Clock.GetUtcNow(), (await db.Games.SingleAsync()).ClosedAt);
            Assert.Equal(CandidateState.Ready, (await db.AnswerCandidates.FindAsync(candidate.Id))!.State);
            Assert.Single(await db.Participants.ToListAsync());
            Assert.Single(await db.Submissions.ToListAsync());
        }
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, pending));
        await using var check = f.Open();
        Assert.Single(await check.ScoreEntries.ToListAsync());
    }

    [Fact]
    public async Task Normal_final_requires_zero_pending_and_snapshot_survives_restart_and_live_data_changes()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        await f.RunAsync(CommandKind.Name, 101, "DQ person");
        await using (var db = f.Open())
        {
            (await db.Participants.SingleAsync(p => p.TelegramUserId == 101)).Disqualified = true;
            await db.SaveChangesAsync();
        }
        var submission = await PhotoTestSupport.SubmitAsync(f);
        await Close(f);
        Assert.Contains("запрещён", (await f.RunAsync(CommandKind.AskFinalize, 999))!.Text);
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission));
        var final = await Ask(f, CommandKind.AskFinalize);
        await Task.WhenAll(Confirm(f, final), Confirm(f, final));
        var reply = (await f.RunAsync(CommandKind.Leaderboard))!.Text;
        Assert.Contains("Твоё место: 1.", reply);
        await using (var db = f.Open())
        {
            var game = await db.Games.SingleAsync();
            Assert.Equal(GameState.Finished, game.State);
            Assert.False(game.ForceFinalized);
            Assert.Equal(f.Clock.GetUtcNow(), game.FinishedAt);
            Assert.Equal(999, game.FinalizedBy);
            Assert.Equal(2, await db.FinalStandings.CountAsync());
            var row = await db.FinalStandings.SingleAsync(r => r.TelegramUserId == 100);
            Assert.Equal(submission.PointsSnapshot, row.Points);
            Assert.Equal(submission.SubmittedAt, row.LastApprovedSubmittedAt);
            Assert.Null((await db.FinalStandings.SingleAsync(r => r.TelegramUserId == 101)).Rank);
            Assert.Single(await db.AuditEvents.Where(e => e.Action == "GameFinalized").ToListAsync());
            // Verify reads use the snapshot, even if someone modifies source tables outside the application.
            var person = await db.Participants.SingleAsync(p => p.TelegramUserId == 100);
            person.DisplayName = "external change";
            person.Disqualified = true;
            (await db.Submissions.SingleAsync()).PointsSnapshot = 999;
            await db.SaveChangesAsync();
        }
        Assert.Equal(reply, (await f.RunAsync(CommandKind.Leaderboard))!.Text);
        Assert.Contains("DQ", (await f.RunAsync(CommandKind.Leaderboard, 101))!.Text);
    }

    [Fact]
    public async Task Force_final_is_distinct_unreviewed_with_no_points_or_returns_and_archive_continues()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f, repeatable: true);
        var submission = await PhotoTestSupport.SubmitAsync(f, album: true);
        var before = await f.ParticipantAsync();
        await Close(f);
        Assert.Null((await f.RunAsync(CommandKind.AskForceFinalize, 999))!.Buttons);
        var request = await Ask(f, CommandKind.AskForceFinalize, "Время проверки истекло");
        await Confirm(f, request);
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission));
        await using (var db = f.Open())
        {
            var game = await db.Games.SingleAsync();
            Assert.True(game.ForceFinalized);
            Assert.Equal("Время проверки истекло", game.FinalReason);
            Assert.Equal(1, game.UnreviewedCount);
            Assert.Equal(SubmissionState.Unreviewed, (await db.Submissions.SingleAsync()).Status);
            Assert.Empty(await db.ScoreEntries.ToListAsync());
            Assert.Equal(0, (await db.FinalStandings.SingleAsync()).Points);
            Assert.Equal(TaskLocation.Consumed, (await db.TaskAvailabilities.SingleAsync(a => a.TaskId == submission.TaskId)).Location);
            Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "TaskReturnedToQueue" || e.Action == "TaskReturnedToPool"));
            Assert.Single(await db.AuditEvents.Where(e => e.Action == "SubmissionUnreviewed" && e.Data == game.FinalReason).ToListAsync());
        }
        var after = await f.ParticipantAsync();
        Assert.Equal(before.NextTaskAvailableAt, after.NextTaskAvailableAt);
        Assert.Equal(before.NextBucket, after.NextBucket);
        Assert.Equal(before.NextPosition, after.NextPosition);
        var gateway = new FakeTelegramGateway();
        await PhotoTestSupport.DrainAsync(f, gateway);
        Assert.Contains(gateway.Replies, r => r.Text.Contains("Не проверено") && r.Buttons is null);
        await using var check = f.Open();
        Assert.All(await check.SubmissionMedia.ToListAsync(), m => Assert.NotNull(m.ArchiveMessageId));
        Assert.Equal(GameState.Finished, (await check.Games.SingleAsync()).State);
    }

    [Fact]
    public async Task Confirmation_checks_roles_chats_cancel_and_changed_pending_count()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var pending = await PhotoTestSupport.SubmitAsync(f);
        var close = await Ask(f, CommandKind.AskClose);
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmTransition, 998) with { EntityId = close });
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmTransition, 999) with { EntityId = close, ChatId = -1, IsPrivate = false });
        await using (var db = f.Open()) Assert.Equal(GameState.Open, (await db.Games.SingleAsync()).State);
        await f.ExecuteAsync(f.Command(CommandKind.CancelTransition, 999) with { EntityId = close });
        await Confirm(f, close);
        await using (var db = f.Open()) Assert.Equal(GameState.Open, (await db.Games.SingleAsync()).State);
        await Close(f);
        var force = await Ask(f, CommandKind.AskForceFinalize, "time");
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, pending));
        Assert.Contains("изменилось", (await Confirm(f, force))!.Text);
        await using (var db = f.Open()) Assert.Equal(GameState.Review, (await db.Games.SingleAsync()).State);
        await Confirm(f, await Ask(f, CommandKind.AskFinalize));
        await Confirm(f, close);
        await Confirm(f, force);
        await f.RunAsync(CommandKind.ConfirmOpen, 999, version: 0);
        await f.RunAsync(CommandKind.Name, name: "late rename");
        await f.RunAsync(CommandKind.AskDisqualify, 999, $"{(await f.ParticipantAsync()).Id} reason");
        await f.RunAsync(CommandKind.Start);
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, pending));
        await using var check = f.Open();
        Assert.Equal(GameState.Finished, (await check.Games.SingleAsync()).State);
        Assert.Single(await check.FinalStandings.ToListAsync());
        Assert.Single(await check.ScoreEntries.ToListAsync());
        Assert.Equal("Монстр", (await check.Participants.SingleAsync()).DisplayName);
        Assert.Empty(await check.DisciplineRequests.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_close_race_has_one_atomic_order(bool confirmFirst)
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        await PhotoTestSupport.PhotoAsync(f, 1);
        var candidate = await PhotoTestSupport.CandidateAsync(f);
        var close = await Ask(f, CommandKind.AskClose);
        // Queue both commands behind the same gate in both orders.
        await f.Gate.Semaphore.WaitAsync();
        var first = confirmFirst ? PhotoTestSupport.ClickAsync(f, candidate) : Confirm(f, close);
        var second = confirmFirst ? Confirm(f, close) : PhotoTestSupport.ClickAsync(f, candidate);
        f.Gate.Semaphore.Release();
        await Task.WhenAll(first, second);
        await using var db = f.Open();
        Assert.Equal(GameState.Review, (await db.Games.SingleAsync()).State);
        Assert.Equal(confirmFirst ? 1 : 0, await db.Submissions.CountAsync());
        Assert.Equal(confirmFirst ? AttemptState.Submitted : AttemptState.Active, (await db.Attempts.SingleAsync()).State);
        Assert.Equal(confirmFirst ? 1 : 0, await db.AuditEvents.CountAsync(e => e.Action == "SubmissionConfirmed"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approve_force_race_never_adds_points_after_snapshot(bool approveFirst)
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var submission = await PhotoTestSupport.SubmitAsync(f);
        await Close(f);
        var force = await Ask(f, CommandKind.AskForceFinalize, "time");
        await f.Gate.Semaphore.WaitAsync();
        var first = approveFirst ? f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission)) : Confirm(f, force);
        var second = approveFirst ? Confirm(f, force) : f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission));
        f.Gate.Semaphore.Release();
        await Task.WhenAll(first, second);
        if (approveFirst) await Confirm(f, await Ask(f, CommandKind.AskFinalize));
        await using var db = f.Open();
        Assert.Equal(GameState.Finished, (await db.Games.SingleAsync()).State);
        Assert.Equal(approveFirst ? submission.PointsSnapshot : 0, (await db.FinalStandings.SingleAsync()).Points);
        Assert.Equal(approveFirst ? 1 : 0, await db.ScoreEntries.CountAsync());
        Assert.Equal(approveFirst ? SubmissionState.Approved : SubmissionState.Unreviewed, (await db.Submissions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_final_transaction_rolls_back_unreviewed_snapshot_audit_and_processed_update()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var submission = await PhotoTestSupport.SubmitAsync(f);
        await Close(f);
        var request = await Ask(f, CommandKind.AskForceFinalize, "time");
        var command = f.Command(CommandKind.ConfirmTransition, 999) with { EntityId = request };
        await using (var db = f.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"update:{command.UpdateId}:reply", Payload = "{}" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => f.ExecuteAsync(command));
        await using (var db = f.Open())
        {
            Assert.Equal(GameState.Review, (await db.Games.SingleAsync()).State);
            Assert.Null((await db.Games.SingleAsync()).FinishedAt);
            Assert.Equal(SubmissionState.Pending, (await db.Submissions.SingleAsync()).Status);
            Assert.Empty(await db.FinalStandings.ToListAsync());
            Assert.True((await db.TransitionRequests.FindAsync(request))!.Active);
            Assert.False(await db.ProcessedUpdates.AnyAsync(p => p.Id == command.UpdateId));
            Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "SubmissionUnreviewed"));
            Assert.Equal(TaskLocation.Pending, (await db.TaskAvailabilities.SingleAsync(a => a.TaskId == submission.TaskId)).Location);
            db.OutgoingActions.Remove(await db.OutgoingActions.SingleAsync(a => a.LogicalKey == $"update:{command.UpdateId}:reply"));
            await db.SaveChangesAsync();
        }
        await f.ExecuteAsync(command);
        await using var check = f.Open();
        Assert.Single(await check.FinalStandings.ToListAsync());
    }
}
