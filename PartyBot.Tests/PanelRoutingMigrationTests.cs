using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Api.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PartyBot.Tests;

public sealed class PanelRoutingMigrationTests
{
    [Theory]
    [InlineData("/close", CommandKind.AskClose)]
    [InlineData("/finalize", CommandKind.AskFinalize)]
    [InlineData("/forcefinalize reason", CommandKind.AskForceFinalize)]
    [InlineData("/diagnostics", CommandKind.Diagnostics)]
    [InlineData("/retry 1", CommandKind.AskRetryDelivery)]
    [InlineData("/rank", CommandKind.Leaderboard)]
    [InlineData("/admin", CommandKind.Admin)]
    [InlineData("/dq 1 reason", CommandKind.AskDisqualify)]
    [InlineData("/reinstate 1 reason", CommandKind.AskReinstate)]
    public void Routes_panel_commands(string text, CommandKind expected)
    {
        var command = UpdateRouter.Map(new Update { Id = 1, Message = new Message { From = new User { Id = 999 }, Chat = new Chat { Id = 999, Type = ChatType.Private }, Text = text } });
        Assert.Equal(expected, command.Kind);
        Assert.Equal(999, command.ActorId);
    }
    [Theory]
    [InlineData("retryyes:no")]
    [InlineData("retry:-1")]
    [InlineData("diagnostics:-1")]
    [InlineData("transitionyes:no")]
    [InlineData("transitionno:extra:extra")]
    [InlineData("disciplineyes:no")]
    [InlineData("disciplineno:")]
    [InlineData("person:-1")]
    [InlineData("admin:2147483648")]
    [InlineData("admin:0:extra")]
    public void Invalid_callbacks_are_ignored(string data)
    {
        var command = UpdateRouter.Map(new Update { Id = 1, CallbackQuery = new CallbackQuery { From = new User { Id = 999 }, Data = data,
            Message = new Message { Chat = new Chat { Id = 999, Type = ChatType.Private } } } });
        Assert.Equal(CommandKind.Ignore, command.Kind);
    }
    [Fact]
    public async Task Upgrade_stage_three_preserves_audit_for_replay_and_participant_state()
    {
        await using var f = new GameFixture();
        var eventId = Guid.NewGuid();
        await using (var db = f.Open())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261018225218_PhotoAnswers");
            db.Participants.Add(new Participant { TelegramUserId = 100, PrivateChatId = 100, DisplayName = "existing", Disqualified = true,
                NextTaskAvailableAt = f.Clock.GetUtcNow().AddMinutes(5) });
            db.AuditEvents.Add(new AuditEvent { Id = eventId, At = f.Clock.GetUtcNow(), ActorId = 999, Action = "ExistingEvent", Data = "preserve" });
            await db.SaveChangesAsync();
        }
        await using var actual = f.Open();
        await new DatabaseBootstrap(actual, f.Clock).InitializeAsync();
        Assert.True((await actual.Participants.SingleAsync()).Disqualified);
        Assert.Equal(f.Clock.GetUtcNow().AddMinutes(5), (await actual.Participants.SingleAsync()).NextTaskAvailableAt);
        Assert.Equal("preserve", (await actual.AuditEvents.FindAsync(eventId))!.Data);
        Assert.Empty(await actual.DisciplineRequests.ToListAsync());
        Assert.False(actual.Database.HasPendingModelChanges());
    }
}
