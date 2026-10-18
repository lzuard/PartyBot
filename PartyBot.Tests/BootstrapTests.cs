using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PartyBot.Api.Persistence;

namespace PartyBot.Tests;

public sealed class BootstrapTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "partybot-tests-" + Guid.NewGuid());

    private BotDbContext Open()
    {
        Directory.CreateDirectory(directory);
        return new BotDbContext(new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "test.db")};Pooling=False;Default Timeout=30")
            .Options);
    }

    [Fact]
    public async Task Clean_database_migrates_and_restart_preserves_timestamp()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(3)));
        var initialTime = time.GetUtcNow();
        await using (var db = Open())
        {
            await new DatabaseBootstrap(db, time).InitializeAsync();
            Assert.Single(await db.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        time.Advance(TimeSpan.FromHours(1));
        await using (var db = Open())
        {
            await new DatabaseBootstrap(db, time).InitializeAsync();
            var row = Assert.Single(await db.BootstrapRecords.ToListAsync());
            Assert.Equal(initialTime, row.CreatedAt);
            Assert.Equal(TimeSpan.Zero, row.CreatedAt.Offset);
            Assert.Single(await db.BootstrapRecords.Where(x => x.CreatedAt < time.GetUtcNow())
                .OrderBy(x => x.CreatedAt).ToListAsync());
        }
    }

    [Fact]
    public async Task Sqlite_constraint_rejects_second_bootstrap_row()
    {
        await using var db = Open();
        await new DatabaseBootstrap(db, TimeProvider.System).InitializeAsync();
        db.BootstrapRecords.Add(new BootstrapRecord { Id = 2, CreatedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
