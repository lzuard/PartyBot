using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PartyBot.Api.Application;
using PartyBot.Api.Configuration;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

public sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "PartyBot.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

public sealed class GameFixture : IAsyncDisposable
{
    private int updateId;

    public GameFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        Settings = new BotOptions
        {
            BotToken = "synthetic-test-value", SuperAdminUserId = 999, OrganizerUserIds = [998],
            ModerationChatId = -1, ArchiveChatId = -2, AuditChatId = -3,
            SkipCooldownMinutes = 1, SubmitCooldownMinutes = 1, DatabasePath = DatabasePath
        };
    }

    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "partybot-stage2-" + Guid.NewGuid());
    public string DatabasePath => Path.Combine(DirectoryPath, "test.db");
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-10-18T18:00:00Z"));
    public CommandGate Gate { get; } = new();
    public TestEnvironment Environment { get; } = new();
    public BotOptions Settings { get; }

    public async ValueTask DisposeAsync()
    {
        Gate.Dispose();
        await Task.CompletedTask;
        Directory.Delete(DirectoryPath, true);
    }

    public BotDbContext Open()
    {
        return new BotDbContext(new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite($"Data Source={DatabasePath};Pooling=False;Foreign Keys=True;Default Timeout=30").Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = Open();
        await new DatabaseBootstrap(db, Clock).InitializeAsync();
        db.Games.Add(new Game());
        db.PollingStates.Add(new PollingState());
        db.TaskDefinitions.AddRange(QueueTests.Catalog());
        await db.SaveChangesAsync();
    }

    public GameCommands Commands(BotDbContext db)
    {
        return new GameCommands(db, Gate, Clock, new SeededRandomSource(42), Options.Create(Settings), Environment);
    }

    public BotCommand Command(CommandKind kind, long actor = 100, string? name = null, Guid? attempt = null,
        int? version = null)
    {
        return new BotCommand(Interlocked.Increment(ref updateId), actor, actor, true, kind, name, attempt, version);
    }

    public async Task<BotReply?> ExecuteAsync(BotCommand command)
    {
        await using var db = Open();
        return await Commands(db).ExecuteAsync(command);
    }

    public Task<BotReply?> RunAsync(CommandKind kind, long actor = 100, string? name = null, Guid? attempt = null,
        int? version = null)
    {
        return ExecuteAsync(Command(kind, actor, name, attempt, version));
    }

    public async Task OpenGameAsync()
    {
        await RunAsync(CommandKind.ConfirmOpen, 999, version: 0);
    }

    public async Task<Participant> ParticipantAsync(long actor = 100)
    {
        await using var db = Open();
        return await db.Participants.Include(p => p.Tasks).ThenInclude(t => t.Task)
            .Include(p => p.Tasks).ThenInclude(t => t.CurrentAttempt).SingleAsync(p => p.TelegramUserId == actor);
    }
}