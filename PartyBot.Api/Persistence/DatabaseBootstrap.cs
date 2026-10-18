using Microsoft.EntityFrameworkCore;

namespace PartyBot.Api.Persistence;

public sealed class DatabaseBootstrap(BotDbContext db, TimeProvider timeProvider)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        if (!await db.BootstrapRecords.AnyAsync(cancellationToken))
        {
            db.BootstrapRecords.Add(new BootstrapRecord { CreatedAt = timeProvider.GetUtcNow() });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
