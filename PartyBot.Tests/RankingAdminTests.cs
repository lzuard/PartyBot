using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Tests;

public sealed class RankingAdminTests
{
    [Fact]
    public async Task Ranking_uses_approved_snapshots_submitted_time_and_stable_id_before_top_ten()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        for (var i = 0; i < 13; i++) await f.RunAsync(CommandKind.Name, 100 + i, "Same");
        await using (var db = f.Open())
        {
            var people = await db.Participants.OrderBy(p => p.Id).ToListAsync();
            var task = await db.TaskDefinitions.FirstAsync();
            void Answer(int index, int points, int submittedMinutes, int approvedMinutes, SubmissionState status)
            {
                var attempt = new Attempt { ParticipantId = people[index].Id, TaskId = task.Id, State = AttemptState.Submitted, TakenAt = f.Clock.GetUtcNow() };
                db.Attempts.Add(attempt);
                db.Submissions.Add(new Submission { AttemptId = attempt.Id, ParticipantId = people[index].Id, TaskId = task.Id,
                    PointsSnapshot = points, Status = status, SubmittedAt = f.Clock.GetUtcNow().AddMinutes(submittedMinutes), DecidedAt = f.Clock.GetUtcNow().AddMinutes(approvedMinutes) });
            }
            Answer(0, 2, 1, 100, SubmissionState.Approved);
            Answer(0, 3, 4, 101, SubmissionState.Approved);
            Answer(1, 5, 5, 6, SubmissionState.Approved);
            Answer(2, 5, 4, 5, SubmissionState.Approved);
            Answer(0, 100, 90, 100, SubmissionState.Rejected);
            Answer(1, 100, 100, 100, SubmissionState.Pending);
            Answer(2, 100, 100, 100, SubmissionState.Unreviewed);
            Answer(3, 1000, 1, 1, SubmissionState.Approved);
            people[3].Disqualified = true;
            await db.SaveChangesAsync();
            var rows = await new Leaderboard(db).ReadAsync();
            Assert.Equal(new[] { people[0].Id, people[2].Id, people[1].Id }, rows.Take(3).Select(r => r.ParticipantId));
            Assert.Equal(5, rows[0].Points);
            Assert.Equal(f.Clock.GetUtcNow().AddMinutes(4), rows[0].LastSubmittedAt);
            Assert.Equal(people.Skip(4).Select(p => p.Id), rows.Skip(3).Select(r => r.ParticipantId));
            Assert.All(rows.Skip(3), r => Assert.Null(r.LastSubmittedAt));
        }
        var reply = await f.RunAsync(CommandKind.Leaderboard, 112);
        Assert.Contains("Твоё место: 12.", reply!.Text);
        Assert.DoesNotContain("11. Same", reply.Text);
        Assert.Contains("дисквалифицирован", (await f.RunAsync(CommandKind.Leaderboard, 103))!.Text);
    }

    private static async Task<Guid> Ask(GameFixture f, bool dq = true)
    {
        var person = await f.ParticipantAsync();
        await f.RunAsync(dq ? CommandKind.AskDisqualify : CommandKind.AskReinstate, 999, $"{person.Id} тестовая причина");
        await using var db = f.Open();
        return await db.DisciplineRequests.Where(r => r.Active && r.Disqualify == dq).Select(r => r.Id).SingleAsync();
    }
    private static Task<BotReply?> Confirm(GameFixture f, Guid request) =>
        f.ExecuteAsync(f.Command(CommandKind.ConfirmDiscipline, 999) with { EntityId = request });

    [Fact]
    public async Task Dq_and_reinstate_preserve_queue_cooldown_history_and_allow_pending_moderation()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var submission = await PhotoTestSupport.SubmitAsync(f);
        var before = await f.ParticipantAsync();
        var request = await Ask(f);
        await Task.WhenAll(Confirm(f, request), Confirm(f, request));
        Assert.True((await f.ParticipantAsync()).Disqualified);
        await f.RunAsync(CommandKind.GetTask);
        await f.RunAsync(CommandKind.AskSkip);
        await PhotoTestSupport.PhotoAsync(f, 999);
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission));
        await using (var db = f.Open())
        {
            Assert.Single(await db.ScoreEntries.ToListAsync());
            Assert.Single(await db.AuditEvents.Where(e => e.Action == "Disqualified").ToListAsync());
            Assert.Empty(await new Leaderboard(db).ReadAsync());
        }
        var afterModeration = await f.ParticipantAsync();
        var restore = await Ask(f, false);
        await Confirm(f, restore);
        var restored = await f.ParticipantAsync();
        Assert.False(restored.Disqualified);
        Assert.Equal(before.NextTaskAvailableAt, restored.NextTaskAvailableAt);
        Assert.Equal(afterModeration.NextBucket, restored.NextBucket);
        Assert.Equal(afterModeration.NextPosition, restored.NextPosition);
        Assert.Equal(afterModeration.Tasks.OrderBy(t => t.TaskId).Select(t => (t.TaskId, t.Location, t.Position, t.CurrentAttemptId)),
            restored.Tasks.OrderBy(t => t.TaskId).Select(t => (t.TaskId, t.Location, t.Position, t.CurrentAttemptId)));
        await using var check = f.Open();
        Assert.Equal(submission.PointsSnapshot, (await new Leaderboard(check).ReadAsync()).Single().Points);
        Assert.Equal(2, await check.AuditEvents.CountAsync(e => e.Action == "Disqualified" || e.Action == "Reinstated"));
    }

    [Fact]
    public async Task Discipline_checks_roles_chats_reason_cancel_versions_and_finished()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        var person = await f.ParticipantAsync();
        foreach (var command in new[] {
            f.Command(CommandKind.AskDisqualify, 998, $"{person.Id} reason"),
            f.Command(CommandKind.AskDisqualify, 999, $"{person.Id} reason") with { ChatId = -1, IsPrivate = false },
            f.Command(CommandKind.AskDisqualify, 999, $"{person.Id}") }) await f.ExecuteAsync(command);
        await using (var db = f.Open()) Assert.Empty(await db.DisciplineRequests.ToListAsync());
        var request = await Ask(f);
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmDiscipline, 998) with { EntityId = request });
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmDiscipline, 999) with { EntityId = request, ChatId = -1, IsPrivate = false });
        Assert.False((await f.ParticipantAsync()).Disqualified);
        await f.ExecuteAsync(f.Command(CommandKind.CancelDiscipline, 999) with { EntityId = request });
        await Confirm(f, request);
        Assert.False((await f.ParticipantAsync()).Disqualified);
        request = await Ask(f);
        await f.RunAsync(CommandKind.Name, name: "changed");
        await Confirm(f, request);
        Assert.False((await f.ParticipantAsync()).Disqualified);
        await using (var db = f.Open())
        {
            (await db.Games.SingleAsync()).State = GameState.Finished;
            await db.SaveChangesAsync();
        }
        await Confirm(f, request);
        await f.RunAsync(CommandKind.AskDisqualify, 999, $"{person.Id} reason");
        Assert.False((await f.ParticipantAsync()).Disqualified);
        Assert.Contains("снимок", (await f.RunAsync(CommandKind.Leaderboard))!.Text);
    }

    [Fact]
    public async Task Admin_panel_paginates_and_is_private_superadmin_only_without_registration()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        for (var i = 0; i < 11; i++) await f.RunAsync(CommandKind.Name, 100 + i, "person");
        var panel = await f.RunAsync(CommandKind.Admin, 999);
        Assert.Equal(10, panel!.Buttons!.SelectMany(r => r).Count(b => b.Data.StartsWith("person:")));
        var next = await f.ExecuteAsync(f.Command(CommandKind.Admin, 999) with { Page = 1 });
        Assert.Single(next!.Buttons!.SelectMany(r => r), b => b.Data.StartsWith("person:"));
        Assert.Null((await f.RunAsync(CommandKind.Admin, 998))!.Buttons);
        Assert.Null(await f.ExecuteAsync(f.Command(CommandKind.Admin, 999) with { ChatId = -1, IsPrivate = false }));
        await using var db = f.Open();
        Assert.Equal(11, await db.Participants.CountAsync());
    }

    [Fact]
    public async Task Discipline_transaction_rolls_back_and_retry_keeps_active_candidate_and_progress()
    {
        await using var f = new GameFixture();
        await PhotoTestSupport.StartAsync(f);
        await PhotoTestSupport.PhotoAsync(f, 55);
        var candidate = await PhotoTestSupport.CandidateAsync(f);
        var before = await f.ParticipantAsync();
        var id = await Ask(f);
        var command = f.Command(CommandKind.ConfirmDiscipline, 999) with { EntityId = id };
        await using (var db = f.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"update:{command.UpdateId}:reply", Payload = "{}" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => f.ExecuteAsync(command));
        await using (var db = f.Open())
        {
            Assert.False((await db.Participants.SingleAsync()).Disqualified);
            Assert.True((await db.DisciplineRequests.SingleAsync()).Active);
            Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "Disqualified"));
            Assert.False(await db.ProcessedUpdates.AnyAsync(e => e.Id == command.UpdateId));
            db.OutgoingActions.Remove(await db.OutgoingActions.SingleAsync(a => a.LogicalKey == $"update:{command.UpdateId}:reply"));
            await db.SaveChangesAsync();
        }
        await f.ExecuteAsync(command);
        await f.RunAsync(CommandKind.ConfirmSkip, attempt: candidate.AttemptId);
        await PhotoTestSupport.ClickAsync(f, candidate);
        await using (var db = f.Open()) Assert.Empty(await db.Submissions.ToListAsync());
        await Confirm(f, await Ask(f, false));
        Assert.Equal(before.NextBucket, (await f.ParticipantAsync()).NextBucket);
        await PhotoTestSupport.ClickAsync(f, candidate);
        await using var check = f.Open();
        Assert.Single(await check.Submissions.ToListAsync());
    }
}
