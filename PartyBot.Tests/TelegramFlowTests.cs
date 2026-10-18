using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PartyBot.Api.Application;
using PartyBot.Api.Delivery;
using PartyBot.Api.Domain;
using PartyBot.Api.Telegram;
using PartyBot.Tests.Fakes;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PartyBot.Tests;

public sealed class TelegramFlowTests
{
    private static Update MessageUpdate(int id, string text, long actor = 100, ChatType chatType = ChatType.Private) => new()
    {
        Id = id,
        Message = new Message
        {
            Id = id, Text = text, From = new User { Id = actor, FirstName = "Test" },
            Chat = new Chat { Id = actor, Type = chatType }
        }
    };
    private static Update CallbackUpdate(int id, string data, long actor = 100) => new()
    {
        Id = id,
        CallbackQuery = new CallbackQuery
        {
            Id = id.ToString(), Data = data, From = new User { Id = actor, FirstName = "Test" },
            ChatInstance = "test",
            Message = new Message { Id = id, Chat = new Chat { Id = actor, Type = ChatType.Private } }
        }
    };

    [Fact]
    public async Task Telegram_route_registers_gets_and_confirms_skip_using_fake_delivery()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.OpenGameAsync();
        var gateway = new FakeTelegramGateway();
        foreach (var update in new[] { MessageUpdate(100, "/start"), MessageUpdate(101, "/name <Монстр>"), CallbackUpdate(102, "get") })
            await fixture.ExecuteAsync(UpdateRouter.Map(update));
        var participant = await fixture.ParticipantAsync();
        var attemptId = participant.Tasks.Single(task => task.Location == TaskLocation.Active).CurrentAttemptId;
        var question = await fixture.ExecuteAsync(UpdateRouter.Map(CallbackUpdate(103, $"skip:{attemptId:N}")));
        var yes = question!.Buttons![0][0].Data;
        var no = question.Buttons[0][1].Data;
        await fixture.ExecuteAsync(UpdateRouter.Map(CallbackUpdate(104, no)));
        Assert.Contains((await fixture.ParticipantAsync()).Tasks, task => task.Location == TaskLocation.Active);
        await fixture.ExecuteAsync(UpdateRouter.Map(CallbackUpdate(105, yes)));
        await using (var db = fixture.Open())
        {
            var delivery = new ReplyDelivery(db, gateway, fixture.Clock, NullLogger<ReplyDelivery>.Instance);
            while (await delivery.DeliverNextAsync()) { }
        }
        Assert.Contains(gateway.Replies, reply => reply.Text.Contains("<Монстр>"));
        Assert.Contains(gateway.Replies, reply => reply.Buttons?.SelectMany(row => row).Any(button => button.Data == yes) == true);
        Assert.DoesNotContain((await fixture.ParticipantAsync()).Tasks, task => task.Location == TaskLocation.Active);
        await using var persisted = fixture.Open();
        Assert.Equal(106, (await persisted.PollingStates.SingleAsync()).NextUpdateId);
        Assert.All(await persisted.OutgoingActions.ToListAsync(), action => Assert.NotNull(action.MessageId));
    }

    [Fact]
    public async Task Delivery_failure_does_not_lose_business_changes_and_retry_survives_restart()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        var update = MessageUpdate(100, "/name Монстр");
        await fixture.ExecuteAsync(UpdateRouter.Map(update));
        var gateway = new FakeTelegramGateway { NextFailure = new IOException("Synthetic failure") };
        await using (var db = fixture.Open())
        {
            Assert.True(await new ReplyDelivery(db, gateway, fixture.Clock, NullLogger<ReplyDelivery>.Instance).DeliverNextAsync());
            Assert.Null((await db.OutgoingActions.SingleAsync()).MessageId);
        }
        await fixture.ExecuteAsync(UpdateRouter.Map(update));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await using (var db = fixture.Open())
        {
            Assert.True(await new ReplyDelivery(db, gateway, fixture.Clock, NullLogger<ReplyDelivery>.Instance).DeliverNextAsync());
            Assert.Single(await db.Participants.ToListAsync());
            Assert.Single(await db.AuditEvents.Where(item => item.Action == "Registered").ToListAsync());
            Assert.Single(await db.OutgoingActions.ToListAsync());
            Assert.Equal(101, (await db.PollingStates.SingleAsync()).NextUpdateId);
        }
        Assert.Single(gateway.Sent);
    }

    [Fact]
    public async Task Blocked_recipient_does_not_block_other_replies()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.RunAsync(CommandKind.Start);
        await fixture.RunAsync(CommandKind.Start, actor: 101);
        var gateway = new FakeTelegramGateway { NextFailure = new ApiRequestException("Blocked", 403) };
        await using var db = fixture.Open();
        var delivery = new ReplyDelivery(db, gateway, fixture.Clock, NullLogger<ReplyDelivery>.Instance);
        Assert.True(await delivery.DeliverNextAsync());
        Assert.True(await delivery.DeliverNextAsync());
        Assert.False(await delivery.DeliverNextAsync());
        Assert.Equal(101, Assert.Single(gateway.Sent).Reference.ChatId);
        Assert.Single(await db.OutgoingActions.Where(item => item.Failed).ToListAsync());
    }

    [Theory]
    [InlineData("skipyes")]
    [InlineData("skipyes:bad-id")]
    [InlineData("skipyes:00000000000000000000000000000000:extra")]
    [InlineData("openyes:bad-version")]
    public void Malformed_callbacks_cannot_be_mapped_to_mutating_commands(string data)
    {
        Assert.Equal(CommandKind.Help, UpdateRouter.Map(CallbackUpdate(1, data)).Kind);
    }

    [Fact]
    public async Task Ignored_updates_are_durable_without_reply_and_without_registration()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        await fixture.ExecuteAsync(UpdateRouter.Map(MessageUpdate(70, "/name Монстр", chatType: ChatType.Group)));
        await fixture.ExecuteAsync(UpdateRouter.Map(new Update { Id = 71 }));
        await using var db = fixture.Open();
        Assert.Empty(await db.Participants.ToListAsync());
        Assert.Empty(await db.OutgoingActions.ToListAsync());
        Assert.Equal(72, (await db.PollingStates.SingleAsync()).NextUpdateId);
    }
}
