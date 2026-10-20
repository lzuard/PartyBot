using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PartyBot.Api.Persistence;

namespace PartyBot.Tests;

public sealed class RecoveryMigrationTests
{
    [Fact]
    public async Task Upgrade_stage_five_preserves_failed_delivery_attempts_and_offset()
    {
        await using var f = new GameFixture();
        await using (var db = f.Open())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261020200107_GameFinalization");
            db.OutgoingActions.Add(new OutgoingAction { LogicalKey = "existing", Payload = "{}", Failed = true, Attempts = 3, LastError = "Telegram 403", RetryAt = f.Clock.GetUtcNow() });
            db.PollingStates.Add(new PollingState { NextUpdateId = 99 });
            await db.SaveChangesAsync();
        }
        await using var actual = f.Open();
        await new DatabaseBootstrap(actual, f.Clock).InitializeAsync();
        Assert.Equal(99, (await actual.PollingStates.SingleAsync()).NextUpdateId);
        var action = await actual.OutgoingActions.SingleAsync();
        Assert.True(action.Failed);
        Assert.Equal(3, action.Attempts);
        Assert.Equal("Telegram 403", action.LastError);
        Assert.Empty(await actual.RecoveryRequests.ToListAsync());
        Assert.False(actual.Database.HasPendingModelChanges());
    }
}
