using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using static PartyBot.Tests.PhotoTestSupport;

namespace PartyBot.Tests;

public sealed class ModerationTests
{
    [Fact]
    public async Task Concurrent_approvals_award_once_return_repeatable_once_and_preserve_N2()
    {
        await using var f = new GameFixture();
        await StartAsync(f, repeatable: true);
        var submission = await SubmitAsync(f);
        var before = await f.ParticipantAsync();
        await Task.WhenAll(f.ExecuteAsync(Moderate(f, submission)), f.ExecuteAsync(Moderate(f, submission, actor: 999)));
        await f.ExecuteAsync(Moderate(f, submission));
        var after = await f.ParticipantAsync();
        Assert.Equal(before.NextTaskAvailableAt, after.NextTaskAvailableAt);
        Assert.Equal(before.NextBucket, after.NextBucket);
        Assert.Equal(TaskLocation.Pool, after.Tasks.Single(x => x.TaskId == submission.TaskId).Location);
        await using var db = f.Open();
        var score = Assert.Single(await db.ScoreEntries.ToListAsync());
        Assert.Equal(submission.PointsSnapshot, score.Points);
        Assert.Equal(SubmissionState.Approved, (await db.Submissions.SingleAsync()).Status);
        Assert.Single(await db.AuditEvents.Where(x => x.Action == "TaskReturnedToPool").ToListAsync());
    }

    [Fact]
    public async Task Reject_requires_owned_live_choice_and_reason_and_returns_oneoff_to_queue()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.AskReject));
        RejectChoice choice;
        await using (var db = f.Open()) choice = await db.RejectChoices.SingleAsync();
        var reject = Moderate(f, submission, CommandKind.ConfirmReject) with { EntityId = choice.Id, ReasonId = "unclear" };
        await f.ExecuteAsync(reject with { UpdateId = f.Command(CommandKind.Help).UpdateId, ActorId = 999 });
        await f.ExecuteAsync(reject with { UpdateId = f.Command(CommandKind.Help).UpdateId, ReasonId = "invalid" });
        await using (var db = f.Open()) Assert.Equal(SubmissionState.Pending, (await db.Submissions.SingleAsync()).Status);
        await f.ExecuteAsync(reject);
        await f.ExecuteAsync(reject with { UpdateId = f.Command(CommandKind.Help).UpdateId });
        await using var actual = f.Open();
        Assert.Equal(SubmissionState.Rejected, (await actual.Submissions.SingleAsync()).Status);
        Assert.Equal(f.Settings.RejectReasons["unclear"], (await actual.Submissions.SingleAsync()).RejectReason);
        Assert.Empty(await actual.ScoreEntries.ToListAsync());
        var participant = await f.ParticipantAsync();
        Assert.Equal(6, PersonalQueue.Queued(participant).Length);
        Assert.Equal(submission.TaskId, PersonalQueue.Queued(participant).Last().TaskId);
    }

    [Fact]
    public async Task Approve_wins_while_reason_is_being_selected_and_cancel_invalidates_choice()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.AskReject));
        RejectChoice choice;
        await using (var db = f.Open()) choice = await db.RejectChoices.SingleAsync();
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.CancelReject) with { EntityId = choice.Id });
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.ConfirmReject) with { EntityId = choice.Id, ReasonId = "unclear" });
        await using (var db = f.Open()) Assert.Equal(SubmissionState.Pending, (await db.Submissions.SingleAsync()).Status);
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.AskReject));
        await using (var db = f.Open()) choice = await db.RejectChoices.SingleAsync(x => x.Active);
        await f.ExecuteAsync(Moderate(f, submission, actor: 999));
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.ConfirmReject) with { EntityId = choice.Id, ReasonId = "unclear" });
        await using var actual = f.Open();
        Assert.Single(await actual.ScoreEntries.ToListAsync());
        Assert.Equal(SubmissionState.Approved, (await actual.Submissions.SingleAsync()).Status);
        Assert.Equal(TaskLocation.Consumed, (await f.ParticipantAsync()).Tasks.Single(x => x.TaskId == submission.TaskId).Location);
    }

    [Fact]
    public async Task Approve_reject_race_has_one_terminal_decision_and_one_return()
    {
        await using var f = new GameFixture();
        await StartAsync(f, repeatable: true);
        var submission = await SubmitAsync(f);
        await f.ExecuteAsync(Moderate(f, submission, CommandKind.AskReject));
        RejectChoice choice;
        await using (var db = f.Open()) choice = await db.RejectChoices.SingleAsync();
        await Task.WhenAll(f.ExecuteAsync(Moderate(f, submission, actor: 999)),
            f.ExecuteAsync(Moderate(f, submission, CommandKind.ConfirmReject) with { EntityId = choice.Id, ReasonId = "unclear" }));
        await using var actual = f.Open();
        var result = await actual.Submissions.SingleAsync();
        Assert.Contains(result.Status, new[] { SubmissionState.Approved, SubmissionState.Rejected });
        Assert.Equal(result.Status == SubmissionState.Approved ? 1 : 0, await actual.ScoreEntries.CountAsync());
        Assert.Single(await actual.AuditEvents.Where(x => x.Action == "TaskReturnedToPool" || x.Action == "TaskReturnedToQueue").ToListAsync());
    }

    [Fact]
    public async Task Roles_chats_finished_and_stale_version_rejected_but_DQ_pending_review_allowed()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        await f.ExecuteAsync(Moderate(f, submission, actor: 100));
        await f.ExecuteAsync(Moderate(f, submission) with { ChatId = -123 });
        await f.ExecuteAsync(Moderate(f, submission) with { EntityVersion = 7 });
        await using (var db = f.Open()) { (await db.Games.SingleAsync()).State = GameState.Finished; await db.SaveChangesAsync(); }
        await f.ExecuteAsync(Moderate(f, submission));
        await using (var db = f.Open())
        {
            Assert.Empty(await db.ScoreEntries.ToListAsync());
            (await db.Games.SingleAsync()).State = GameState.Review;
            (await db.Participants.SingleAsync()).Disqualified = true;
            await db.SaveChangesAsync();
        }
        await f.ExecuteAsync(Moderate(f, submission));
        await using var actual = f.Open();
        Assert.Single(await actual.ScoreEntries.ToListAsync());
    }
    [Fact]
    public async Task Decision_score_return_audit_and_update_roll_back_together_on_failure()
    {
        await using var f = new GameFixture();
        await StartAsync(f, repeatable: true);
        var submission = await SubmitAsync(f);
        var approve = Moderate(f, submission);
        await using (var db = f.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"submission:{submission.Id}:RefreshArchive", Payload = "{}" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => f.ExecuteAsync(approve));
        await using (var db = f.Open())
        {
            Assert.Equal(SubmissionState.Pending, (await db.Submissions.SingleAsync()).Status);
            Assert.Empty(await db.ScoreEntries.ToListAsync());
            Assert.False(await db.ProcessedUpdates.AnyAsync(x => x.Id == approve.UpdateId));
            Assert.False(await db.AuditEvents.AnyAsync(x => x.Action == "ScoreAwarded"));
            Assert.Equal(TaskLocation.Pending, (await db.TaskAvailabilities.SingleAsync(x => x.TaskId == submission.TaskId)).Location);
            db.OutgoingActions.Remove(await db.OutgoingActions.SingleAsync(x => x.LogicalKey == $"submission:{submission.Id}:RefreshArchive"));
            await db.SaveChangesAsync();
        }
        await f.ExecuteAsync(approve);
        await using var actual = f.Open();
        Assert.Single(await actual.ScoreEntries.ToListAsync());
        Assert.Equal(SubmissionState.Approved, (await actual.Submissions.SingleAsync()).Status);
    }

}
