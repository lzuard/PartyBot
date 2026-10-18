using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartyBot.Api.Application;
using PartyBot.Api.Delivery;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;

namespace PartyBot.Tests;

internal static class PhotoTestSupport
{
    public static async Task StartAsync(GameFixture fixture, bool repeatable = false)
    {
        await fixture.InitializeAsync();
        if (repeatable)
        {
            await using var db = fixture.Open();
            foreach (var task in await db.TaskDefinitions.ToListAsync()) task.Repeatable = true;
            await db.SaveChangesAsync();
        }
        await fixture.OpenGameAsync();
        await fixture.RunAsync(CommandKind.Name, name: "Монстр");
        await fixture.RunAsync(CommandKind.GetTask);
    }
    public static Task<BotReply?> PhotoAsync(GameFixture fixture, int messageId, string? group = null, long actor = 100) =>
        fixture.ExecuteAsync(fixture.Command(CommandKind.Photo, actor) with { Photo = new IncomingPhoto(messageId, $"file-{messageId}", $"unique-{messageId}", group, $"caption-{messageId}") });
    public static async Task ReadyAsync(GameFixture fixture)
    {
        await using var db = fixture.Open();
        await new CandidateReadiness(db, fixture.Gate, fixture.Clock).ProcessAsync();
    }
    public static async Task<AnswerCandidate> CandidateAsync(GameFixture fixture)
    {
        await using var db = fixture.Open();
        return await db.AnswerCandidates.Include(x => x.Media).SingleAsync(x => x.State == CandidateState.Ready || x.State == CandidateState.Collecting);
    }
    public static Task<BotReply?> ClickAsync(GameFixture fixture, AnswerCandidate candidate, CommandKind kind = CommandKind.ConfirmAnswer, long actor = 100) =>
        fixture.ExecuteAsync(fixture.Command(kind, actor) with { EntityId = candidate.Id, EntityVersion = candidate.Version });
    public static async Task<Submission> SubmitAsync(GameFixture fixture, bool album = false)
    {
        await PhotoAsync(fixture, 101, album ? "album" : null);
        if (album)
        {
            await PhotoAsync(fixture, 103, "album");
            await PhotoAsync(fixture, 102, "album");
            fixture.Clock.Advance(TimeSpan.FromSeconds(fixture.Settings.AlbumDebounceSeconds));
            await ReadyAsync(fixture);
        }
        await ClickAsync(fixture, await CandidateAsync(fixture));
        await using var db = fixture.Open();
        return await db.Submissions.Include(x => x.Media).SingleAsync();
    }
    public static BotCommand Moderate(GameFixture fixture, Submission submission, CommandKind kind = CommandKind.Approve, long actor = 998) =>
        fixture.Command(kind, actor) with { ChatId = fixture.Settings.ModerationChatId, IsPrivate = false, EntityId = submission.Id, EntityVersion = submission.Version };
    public static async Task<bool> DeliverAsync(GameFixture fixture, FakeTelegramGateway gateway)
    {
        await using var db = fixture.Open();
        gateway.BeforeNetwork = () => Assert.Null(db.Database.CurrentTransaction);
        return await new PhotoDelivery(db, fixture.Gate, gateway, fixture.Clock, Options.Create(fixture.Settings), NullLogger<PhotoDelivery>.Instance).DeliverNextAsync();
    }
    public static async Task DrainAsync(GameFixture fixture, FakeTelegramGateway gateway)
    {
        for (var i = 0; i < 100; i++) if (!await DeliverAsync(fixture, gateway)) return;
        Assert.Fail("Delivery loop did not settle.");
    }
}
