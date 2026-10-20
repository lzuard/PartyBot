using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PartyBot.Api.Application;
using PartyBot.Api.Delivery;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;
using Telegram.Bot.Exceptions;
using static PartyBot.Tests.PhotoTestSupport;

namespace PartyBot.Tests;

public sealed class RecoveryTests
{
    private static async Task<Guid> Ask(GameFixture f, long id)
    {
        var reply = await f.RunAsync(CommandKind.AskRetryDelivery, 999, id.ToString());
        return Guid.ParseExact(reply!.Buttons![0][0].Data.Split(':')[1], "N");
    }
    private static Task<BotReply?> Confirm(GameFixture f, Guid request) => f.ExecuteAsync(f.Command(CommandKind.ConfirmRetryDelivery, 999) with { EntityId = request });
    private static async Task<long> ActionId(GameFixture f, DeliveryKind kind)
    {
        await using var db = f.Open();
        return await db.OutgoingActions.Where(a => a.Kind == kind).Select(a => a.Id).SingleAsync();
    }

    [Fact]
    public async Task Archive_failure_can_fail_again_then_recover_without_duplicate_alert_key_or_submission()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, true);
        var gateway = new FakeTelegramGateway();
        await DeliverAsync(f, gateway); // old candidate prompt is suppressed
        gateway.NextFailure = new ApiRequestException("Forbidden", 403);
        await DeliverAsync(f, gateway);
        var id = await ActionId(f, DeliveryKind.ArchiveMedia);
        var diagnostics = await f.RunAsync(CommandKind.Diagnostics, 999);
        Assert.Contains("Неполный архив: 1", diagnostics!.Text);
        Assert.Contains("Failed: 1", diagnostics.Text);
        await Confirm(f, await Ask(f, id));
        gateway.NextFailure = new ApiRequestException("Forbidden", 403);
        await DeliverAsync(f, gateway);
        await Confirm(f, await Ask(f, id));
        await DrainAsync(f, gateway);
        await using var db = f.Open();
        Assert.All(await db.SubmissionMedia.ToListAsync(), m => Assert.NotNull(m.ArchiveMessageId));
        Assert.NotNull((await db.Submissions.SingleAsync()).WorkCardId);
        Assert.Equal(2, await db.OutgoingActions.CountAsync(a => a.LogicalKey.StartsWith($"delivery:{id}:alert:")));
        Assert.Single(await db.Submissions.ToListAsync());
        Assert.Equal(submission.Id, (await db.Submissions.SingleAsync()).Id);
    }

    [Fact]
    public async Task Work_media_recovery_rearms_skipped_card_after_restart()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await SubmitAsync(f);
        var gateway = new FakeTelegramGateway();
        await DeliverAsync(f, gateway); // prompt
        await DeliverAsync(f, gateway); // archive media
        await DeliverAsync(f, gateway); // archive card
        gateway.NextFailure = new ApiRequestException("Forbidden", 403);
        await DeliverAsync(f, gateway); // failed work media
        await DeliverAsync(f, gateway); // card settles with no media
        await using (var db = f.Open())
        {
            Assert.True((await db.OutgoingActions.SingleAsync(a => a.Kind == DeliveryKind.WorkCard)).Done);
            Assert.Null((await db.Submissions.SingleAsync()).WorkCardId);
        }
        await Confirm(f, await Ask(f, await ActionId(f, DeliveryKind.WorkMedia)));
        await DrainAsync(f, gateway);
        await using var actual = f.Open();
        Assert.NotNull((await actual.Submissions.SingleAsync()).WorkCardId);
        Assert.Equal(2, gateway.Replies.Count); // archive and work card, no duplicate archive
    }

    [Fact]
    public async Task Cleanup_recovery_after_finished_keeps_snapshot_and_decision_and_known_refs()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        await f.ExecuteAsync(Moderate(f, submission));
        await FinalizationTests.Close(f);
        await FinalizationTests.Confirm(f, await FinalizationTests.Ask(f, CommandKind.AskFinalize));
        gateway.DeleteFailure = new ApiRequestException("Forbidden", 403);
        await DrainAsync(f, gateway);
        await using (var db = f.Open()) Assert.True((await db.OutgoingActions.SingleAsync(a => a.Kind == DeliveryKind.CleanupWork)).Failed);
        gateway.DeleteFailure = null;
        var request = await Ask(f, await ActionId(f, DeliveryKind.CleanupWork));
        await Task.WhenAll(Confirm(f, request), Confirm(f, request));
        await DrainAsync(f, gateway);
        await using var actual = f.Open();
        Assert.Single(await actual.ScoreEntries.ToListAsync());
        Assert.Equal(submission.PointsSnapshot, (await actual.FinalStandings.SingleAsync()).Points);
        Assert.Single(await actual.AuditEvents.Where(e => e.Action == "DeliveryRetryRequested").ToListAsync());
        Assert.All(await actual.DeliveryMessages.Where(m => m.IsWork).ToListAsync(), m => Assert.True(m.Deleted));
        Assert.Equal(GameState.Finished, (await actual.Games.SingleAsync()).State);
    }

    [Fact]
    public async Task Recovery_checks_owner_chat_cancel_staleness_and_never_bypasses_retry_after()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        long id;
        await using (var db = f.Open())
        {
            var action = new OutgoingAction { LogicalKey = "test", Payload = "{}", Failed = true, Attempts = 1 };
            db.OutgoingActions.Add(action);
            await db.SaveChangesAsync();
            id = action.Id;
        }
        var request = await Ask(f, id);
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmRetryDelivery, 998) with { EntityId = request });
        await f.ExecuteAsync(f.Command(CommandKind.ConfirmRetryDelivery, 999) with { EntityId = request, ChatId = -1, IsPrivate = false });
        await using (var db = f.Open()) Assert.True((await db.OutgoingActions.FindAsync(id))!.Failed);
        await f.ExecuteAsync(f.Command(CommandKind.CancelRetryDelivery, 999) with { EntityId = request });
        await Confirm(f, request);
        await using (var db = f.Open()) Assert.True((await db.OutgoingActions.FindAsync(id))!.Failed);
        request = await Ask(f, id);
        await using (var db = f.Open())
        {
            var a = (await db.OutgoingActions.FindAsync(id))!;
            a.Attempts++;
            await db.SaveChangesAsync();
        }
        await Confirm(f, request);
        await using (var db = f.Open())
        {
            var a = (await db.OutgoingActions.FindAsync(id))!;
            Assert.True(a.Failed);
            a.Failed = false;
            a.RetryAt = f.Clock.GetUtcNow().AddHours(1);
            await db.SaveChangesAsync();
        }
        Assert.Null((await f.RunAsync(CommandKind.AskRetryDelivery, 999, id.ToString()))!.Buttons);
        await using var actual = f.Open();
        Assert.Equal(f.Clock.GetUtcNow().AddHours(1), (await actual.OutgoingActions.FindAsync(id))!.RetryAt);
    }

    [Fact]
    public async Task Diagnostics_does_not_expose_payloads_and_retry_rollback_keeps_failed_action()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        long id;
        await using (var db = f.Open())
        {
            var action = new OutgoingAction { LogicalKey = "sensitive", Payload = "SECRET-PAYLOAD", Failed = true, Attempts = 1, LastError = "Telegram 403" };
            db.OutgoingActions.Add(action);
            await db.SaveChangesAsync();
            id = action.Id;
        }
        Assert.DoesNotContain("SECRET", (await f.RunAsync(CommandKind.Diagnostics, 999))!.Text);
        Assert.Null((await f.RunAsync(CommandKind.Diagnostics, 998))!.Buttons);
        var request = await Ask(f, id);
        var command = f.Command(CommandKind.ConfirmRetryDelivery, 999) with { EntityId = request };
        await using (var db = f.Open())
        {
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = $"update:{command.UpdateId}:reply", Payload = "{}" });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => f.ExecuteAsync(command));
        await using var check = f.Open();
        Assert.True((await check.OutgoingActions.FindAsync(id))!.Failed);
        Assert.True((await check.RecoveryRequests.FindAsync(request))!.Active);
        Assert.False(await check.AuditEvents.AnyAsync(e => e.Action == "DeliveryRetryRequested"));
    }

    [Fact]
    public async Task Text_failed_delivery_recovers_once_and_done_delivery_cannot_be_replayed()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        await f.RunAsync(CommandKind.Name, name: "registered");
        var gateway = new FakeTelegramGateway { NextFailure = new ApiRequestException("Forbidden", 403) };
        await using (var db = f.Open()) await new ReplyDelivery(db, gateway, f.Clock, NullLogger<ReplyDelivery>.Instance).DeliverNextAsync();
        long id;
        await using (var db = f.Open()) id = await db.OutgoingActions.Where(a => a.Failed).Select(a => a.Id).SingleAsync();
        await Confirm(f, await Ask(f, id));
        await using (var db = f.Open()) await new ReplyDelivery(db, gateway, f.Clock, NullLogger<ReplyDelivery>.Instance).DeliverNextAsync();
        Assert.Null((await f.RunAsync(CommandKind.AskRetryDelivery, 999, id.ToString()))!.Buttons);
        Assert.Single(gateway.Sent);
        await using var check = f.Open();
        Assert.Single(await check.Participants.ToListAsync());
    }

    [Fact]
    public async Task Cleanup_resumes_after_partial_deletion_without_repeating_saved_deletes()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, true);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        await f.ExecuteAsync(Moderate(f, submission));
        var calls = 0;
        gateway.BeforeDelete = _ => { if (++calls == 2) throw new IOException("disconnected midway"); };
        await DrainAsync(f, gateway);
        Assert.Single(gateway.Deleted);
        await using (var db = f.Open()) Assert.Single(await db.DeliveryMessages.Where(m => m.Deleted).ToListAsync());
        gateway.BeforeDelete = null;
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync(f, gateway);
        Assert.Equal(4, gateway.Deleted.Count);
        Assert.Equal(4, gateway.Deleted.Distinct().Count());
        await using var check = f.Open();
        Assert.Single(await check.ScoreEntries.ToListAsync());
        Assert.True((await check.OutgoingActions.SingleAsync(a => a.Kind == DeliveryKind.CleanupWork)).Done);
    }
}
