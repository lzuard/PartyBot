using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using static PartyBot.Tests.PhotoTestSupport;

namespace PartyBot.Tests;

public sealed class PhotoCommandTests
{
    [Fact]
    public async Task Album_debounce_extends_survives_restart_and_preserves_message_order()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await PhotoAsync(f, 103, "group");
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await PhotoAsync(f, 101, "group");
        var collecting = await CandidateAsync(f);
        await ClickAsync(f, collecting);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await ReadyAsync(f);
        Assert.Equal(CandidateState.Collecting, (await CandidateAsync(f)).State);
        await PhotoAsync(f, 102, "group");
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        await ReadyAsync(f);
        await ReadyAsync(f);
        var ready = await CandidateAsync(f);
        Assert.Equal(CandidateState.Ready, ready.State);
        await ClickAsync(f, collecting); // stale version
        await using (var db = f.Open()) Assert.Empty(await db.Submissions.ToListAsync());
        await ClickAsync(f, ready);
        await using (var db = f.Open())
        {
            var submission = await db.Submissions.Include(x => x.Media).SingleAsync();
            Assert.Equal(new[] { 101, 102, 103 }, submission.Media.OrderBy(x => x.Order).Select(x => x.SourceMessageId));
            Assert.Equal(new[] { "caption-101", "caption-102", "caption-103" }, submission.Media.OrderBy(x => x.Order).Select(x => x.CaptionSnapshot));
            Assert.Single(await db.OutgoingActions.Where(x => x.Kind == DeliveryKind.CandidatePrompt).ToListAsync());
        }
    }

    [Fact]
    public async Task Replacement_foreign_callback_duplicate_source_and_skip_invalidate_old_candidates()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await PhotoAsync(f, 100);
        var first = await CandidateAsync(f);
        await PhotoAsync(f, 100); // same source message under a different update ID
        Assert.Equal(first.Id, (await CandidateAsync(f)).Id);
        await PhotoAsync(f, 101);
        var second = await CandidateAsync(f);
        Assert.NotEqual(first.Id, second.Id);
        await ClickAsync(f, first);
        await f.RunAsync(CommandKind.Name, actor: 101, name: "Другой");
        await f.RunAsync(CommandKind.GetTask, actor: 101);
        await ClickAsync(f, second, actor: 101);
        await ClickAsync(f, second, CommandKind.ReplaceAnswer);
        await ClickAsync(f, second);
        await PhotoAsync(f, 102);
        var third = await CandidateAsync(f);
        await f.RunAsync(CommandKind.ConfirmSkip, attempt: third.AttemptId);
        await ClickAsync(f, third);
        await using var db = f.Open();
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.Equal(CandidateState.Cancelled, (await db.AnswerCandidates.FindAsync(third.Id))!.State);
        Assert.Single(await db.Attempts.Where(x => x.State == AttemptState.Skipped).ToListAsync());
    }

    [Fact]
    public async Task Confirm_is_atomic_idempotent_and_starts_N2_without_waiting_for_moderation()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await PhotoAsync(f, 100);
        var candidate = await CandidateAsync(f);
        var command = f.Command(CommandKind.ConfirmAnswer) with { EntityId = candidate.Id, EntityVersion = candidate.Version };
        await Task.WhenAll(f.ExecuteAsync(command), f.ExecuteAsync(command));
        await ClickAsync(f, candidate);
        var participant = await f.ParticipantAsync();
        Assert.Equal(f.Clock.GetUtcNow().AddMinutes(1), participant.NextTaskAvailableAt);
        Assert.Single(participant.Tasks, x => x.Location == TaskLocation.Pending);
        Assert.DoesNotContain(participant.Tasks, x => x.Location == TaskLocation.Active);
        f.Clock.Advance(TimeSpan.FromSeconds(59));
        await f.RunAsync(CommandKind.GetTask);
        Assert.DoesNotContain((await f.ParticipantAsync()).Tasks, x => x.Location == TaskLocation.Active);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await f.RunAsync(CommandKind.GetTask);
        Assert.Contains((await f.ParticipantAsync()).Tasks, x => x.Location == TaskLocation.Active);
        await using var db = f.Open();
        Assert.Equal(SubmissionState.Pending, (await db.Submissions.SingleAsync()).Status);
        Assert.Single(await db.AuditEvents.Where(x => x.Action == "SubmissionConfirmed").ToListAsync());
        Assert.Equal(4, await db.OutgoingActions.CountAsync(x => x.EntityId == db.Submissions.Select(s => s.Id).First()));
    }

    [Fact]
    public async Task Late_parts_of_confirmed_or_replaced_album_never_attach_to_a_new_attempt()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, album: true);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await f.RunAsync(CommandKind.GetTask);
        await PhotoAsync(f, 104, "album");
        await PhotoAsync(f, 200, "replacement");
        await PhotoAsync(f, 201);
        await PhotoAsync(f, 202, "replacement");
        await using var db = f.Open();
        Assert.Equal(3, await db.SubmissionMedia.CountAsync(x => x.SubmissionId == submission.Id));
        Assert.Equal(201, (await CandidateAsync(f)).Media.Single().SourceMessageId);
        Assert.Equal(3, await db.AnswerCandidates.CountAsync());
    }

    [Theory]
    [InlineData(GameState.Registration)]
    [InlineData(GameState.Review)]
    [InlineData(GameState.Finished)]
    public async Task Closed_game_does_not_accept_new_photos_or_old_confirmation(GameState state)
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await PhotoAsync(f, 100);
        var candidate = await CandidateAsync(f);
        await using (var db = f.Open()) { (await db.Games.SingleAsync()).State = state; await db.SaveChangesAsync(); }
        await ClickAsync(f, candidate);
        await PhotoAsync(f, 101);
        await using var actual = f.Open();
        Assert.Empty(await actual.Submissions.ToListAsync());
        Assert.Single(await actual.CandidateMedia.ToListAsync());
    }

    [Fact]
    public async Task No_active_or_DQ_cannot_submit_and_failed_commit_rolls_back_confirmation()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        await f.OpenGameAsync();
        await f.RunAsync(CommandKind.Name, name: "Монстр");
        await PhotoAsync(f, 100);
        await using (var db = f.Open()) Assert.Empty(await db.AnswerCandidates.ToListAsync());
        await f.RunAsync(CommandKind.GetTask);
        await PhotoAsync(f, 101);
        var candidate = await CandidateAsync(f);
        await using (var db = f.Open()) { (await db.Participants.SingleAsync()).Disqualified = true; await db.SaveChangesAsync(); }
        await ClickAsync(f, candidate);
        await using (var db = f.Open()) { Assert.Empty(await db.Submissions.ToListAsync()); (await db.Participants.SingleAsync()).Disqualified = false; await db.SaveChangesAsync(); }
        var confirm = f.Command(CommandKind.ConfirmAnswer) with { EntityId = candidate.Id, EntityVersion = candidate.Version };
        await using (var db = f.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"update:{confirm.UpdateId}:reply", Payload = "{}" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => f.ExecuteAsync(confirm));
        await using var actual = f.Open();
        Assert.Empty(await actual.Submissions.ToListAsync());
        Assert.Equal(CandidateState.Ready, (await actual.AnswerCandidates.SingleAsync()).State);
        Assert.Equal(AttemptState.Active, (await actual.Attempts.SingleAsync()).State);
        Assert.Null((await actual.Participants.SingleAsync()).NextTaskAvailableAt);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mixed_photo_video_album_is_not_silently_accepted_as_photo_subset(bool photoFirst)
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        if (photoFirst) await PhotoAsync(f, 100, "mixed");
        await f.ExecuteAsync(f.Command(CommandKind.UnsupportedMedia) with { UnsupportedGroupId = "mixed" });
        if (!photoFirst) await PhotoAsync(f, 100, "mixed");
        f.Clock.Advance(TimeSpan.FromSeconds(3));
        await ReadyAsync(f);
        await using var db = f.Open();
        Assert.Empty(await db.AnswerCandidates.Where(x => x.State == CandidateState.Ready || x.State == CandidateState.Collecting).ToListAsync());
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.Empty(await db.OutgoingActions.Where(x => x.Kind == DeliveryKind.CandidatePrompt).ToListAsync());
    }

}
