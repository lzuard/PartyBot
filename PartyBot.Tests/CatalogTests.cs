using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;

namespace PartyBot.Tests;

public sealed class CatalogTests
{
    [Fact]
    public async Task Initial_catalog_is_persisted_and_restart_does_not_import_changed_file()
    {
        await using var fixture = new GameFixture();
        fixture.Settings.CatalogPath = Path.Combine(fixture.DirectoryPath, "catalog.json");
        await File.WriteAllTextAsync(fixture.Settings.CatalogPath, """
            { "demonstrationOnly": true, "tasks": [ { "stableId": "test", "text": "Demo", "repeatable": false } ] }
            """);
        await using (var db = fixture.Open())
        {
            await new DatabaseBootstrap(db, fixture.Clock).InitializeAsync();
            await new CatalogBootstrap(db, Options.Create(fixture.Settings), fixture.Environment, fixture.Clock).InitializeAsync();
            Assert.Equal(1, (await db.TaskDefinitions.SingleAsync()).Points);
        }
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.OpenGameAsync();
        await File.WriteAllTextAsync(fixture.Settings.CatalogPath, "invalid JSON");
        await using (var db = fixture.Open())
        {
            await new CatalogBootstrap(db, Options.Create(fixture.Settings), fixture.Environment, fixture.Clock).InitializeAsync();
            Assert.Equal(GameState.Open, (await db.Games.SingleAsync()).State);
            Assert.Equal("Demo", (await db.TaskDefinitions.SingleAsync()).Text);
            Assert.Single(await db.TaskAvailabilities.ToListAsync());
        }
    }

    [Fact]
    public void Catalog_rejects_duplicate_ids_nonpositive_points_empty_text_and_empty_list()
    {
        foreach (var tasks in new CatalogTask[][]
        {
            [], [new("same", "A"), new("same", "B")], [new("id", "A", 0)],
            [new("id", "A", -1)], [new("id", "")], [new(" ", "A")]
        })
            Assert.Throws<InvalidOperationException>(() => CatalogBootstrap.Validate(new(false, tasks)));
    }

    [Fact]
    public async Task Production_cannot_open_demonstration_catalog()
    {
        await using var fixture = new GameFixture();
        await fixture.InitializeAsync();
        fixture.Environment.EnvironmentName = "Production";
        await using (var db = fixture.Open())
        {
            (await db.Games.SingleAsync()).DemonstrationCatalog = true;
            await db.SaveChangesAsync();
        }
        await fixture.OpenGameAsync();
        await using var actual = fixture.Open();
        Assert.Equal(GameState.Registration, (await actual.Games.SingleAsync()).State);
    }
}
