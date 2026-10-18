using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Api.Application;

public sealed record CatalogTask(string StableId, string Text, int Points = 1, bool Repeatable = false);
public sealed record CatalogDocument(bool DemonstrationOnly, CatalogTask[] Tasks);

public sealed class CatalogBootstrap(BotDbContext db, IOptions<BotOptions> options, IHostEnvironment environment, TimeProvider time)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Existing catalogs are authoritative. Restarts never rebuild participants' pools.
        if (await db.Games.AnyAsync(cancellationToken)) return;
        var path = Path.GetFullPath(options.Value.CatalogPath, environment.ContentRootPath);
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<CatalogDocument>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidOperationException("Catalog is missing.");
        Validate(catalog);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Games.Add(new Game { DemonstrationCatalog = catalog.DemonstrationOnly });
        db.PollingStates.Add(new PollingState());
        db.TaskDefinitions.AddRange(catalog.Tasks.Select(task => new TaskDefinition
        {
            Id = task.StableId, Text = task.Text, Points = task.Points, Repeatable = task.Repeatable
        }));
        db.AuditEvents.Add(new AuditEvent { At = time.GetUtcNow(), Action = "CatalogImported" });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static void Validate(CatalogDocument catalog)
    {
        if (catalog.Tasks is not { Length: > 0 } || catalog.Tasks.Any(task => task is null ||
            string.IsNullOrWhiteSpace(task.StableId) || task.StableId != task.StableId.Trim() ||
            string.IsNullOrWhiteSpace(task.Text) || task.Points <= 0) ||
            catalog.Tasks.Select(task => task.StableId).Distinct(StringComparer.Ordinal).Count() != catalog.Tasks.Length)
            throw new InvalidOperationException("Catalog requires unique stable IDs, non-empty text and positive integer points.");
    }
}
