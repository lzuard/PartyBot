using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Delivery;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;
using Telegram.Bot.Exceptions;

namespace PartyBot.Tests;

public sealed class AuditProjectionTests
{
    private static async Task<int> Project(GameFixture f)
    {
        await using var db = f.Open();
        return await new AuditProjection(db, f.Gate, f.Clock, Options.Create(f.Settings)).EnqueueAsync();
    }
    private static async Task<bool> Deliver(GameFixture f, FakeTelegramGateway gateway)
    {
        await using var db = f.Open();
        gateway.BeforeNetwork = () => Assert.Null(db.Database.CurrentTransaction);
        return await new ReplyDelivery(db, gateway, f.Clock, NullLogger<ReplyDelivery>.Instance).DeliverNextAsync();
    }
    [Fact]
    public async Task Journal_replays_history_once_with_actor_ids_reason_and_thread_and_retries_after_restart()
    {
        await using var f = new GameFixture();
        f.Settings.AuditThreadId = 42;
        await PhotoTestSupport.StartAsync(f);
        var submission = await PhotoTestSupport.SubmitAsync(f);
        await f.ExecuteAsync(PhotoTestSupport.Moderate(f, submission));
        await using (var db = f.Open())
        {
            // Settle unrelated user replies; only journal delivery is under test below.
            foreach (var a in await db.OutgoingActions.Where(a => a.Kind == DeliveryKind.Text).ToListAsync()) a.MessageId = 123;
            await db.SaveChangesAsync();
        }
        Assert.True(await Project(f) > 0);
        Assert.Equal(0, await Project(f));
        await using (var db = f.Open())
        {
            Assert.Equal(await db.AuditEvents.CountAsync(), await db.OutgoingActions.CountAsync(a => a.LogicalKey.StartsWith("audit:")));
            var e = await db.AuditEvents.SingleAsync(e => e.Action == "SubmissionApproved");
            var action = await db.OutgoingActions.SingleAsync(a => a.EntityId == e.Id);
            var payload = JsonSerializer.Deserialize<BotReply>(action.Payload)!;
            Assert.Equal(-3, action.ChatId);
            Assert.Equal(42, payload.MessageThreadId);
            Assert.Contains("Actor: 998", payload.Text);
            Assert.Contains(submission.Id.ToString("N"), payload.Text);
            Assert.Contains(submission.AttemptId.ToString("N"), payload.Text);
            Assert.Contains("21:00:00 +03:00", payload.Text);
        }
        var gateway = new FakeTelegramGateway { NextFailure = new IOException("offline") };
        Assert.True(await Deliver(f, gateway));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        while (await Deliver(f, gateway)) { }
        Assert.All(gateway.Sent, sent => Assert.Equal(-3, sent.Reference.ChatId));
        Assert.All(gateway.Replies, reply => Assert.Equal(42, reply.MessageThreadId));
        Assert.Equal(0, await Project(f));
        await using var check = f.Open();
        Assert.Single(await check.ScoreEntries.ToListAsync());
        Assert.All(await check.OutgoingActions.Where(a => a.LogicalKey.StartsWith("audit:")).ToListAsync(), a => Assert.NotNull(a.MessageId));
    }

    [Fact]
    public async Task Permanent_journal_failure_does_not_rollback_business_or_reenqueue_failed_event()
    {
        await using var f = new GameFixture();
        await f.InitializeAsync();
        await f.RunAsync(CommandKind.Name, name: "first");
        var gateway = new FakeTelegramGateway();
        await Deliver(f, gateway);
        await Project(f);
        gateway.NextFailure = new ApiRequestException("Forbidden", 403);
        await Deliver(f, gateway);
        await f.RunAsync(CommandKind.Name, name: "second");
        await Project(f);
        await using var db = f.Open();
        Assert.Equal("second", (await db.Participants.SingleAsync()).DisplayName);
        Assert.Equal(2, await db.AuditEvents.CountAsync());
        var failed = await db.OutgoingActions.SingleAsync(a => a.Failed);
        Assert.Equal("Telegram 403", failed.LastError);
        Assert.True(await db.OutgoingActions.AnyAsync(a => a.LogicalKey.StartsWith("audit-alert:") && a.ChatId == 999));
        Assert.Single(await db.OutgoingActions.Where(a => a.EntityId == failed.EntityId).ToListAsync());
    }
}
