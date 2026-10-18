using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Application;
using PartyBot.Api.Domain;
using PartyBot.Api.Persistence;
using PartyBot.Tests.Fakes;
using Telegram.Bot.Exceptions;
using static PartyBot.Tests.PhotoTestSupport;

namespace PartyBot.Tests;

public sealed class PhotoDeliveryTests
{
    [Fact]
    public async Task Full_album_is_archived_copied_to_board_updated_and_cleaned_after_approval()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, album: true);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        Assert.Equal(3, Assert.Single(gateway.Albums).Photos.Count);
        Assert.Equal(f.Settings.ArchiveChatId, gateway.Albums[0].ChatId);
        Assert.Equal(f.Settings.ArchiveChatId, Assert.Single(gateway.Copies).SourceChatId);
        Assert.Contains(gateway.Replies, card => card.Buttons?.SelectMany(x => x).Any(x => x.Text == "Approve") == true);
        await f.ExecuteAsync(Moderate(f, submission));
        await DrainAsync(f, gateway);
        await using var db = f.Open();
        var stored = await db.Submissions.Include(x => x.Media).SingleAsync();
        Assert.NotNull(stored.ArchiveCardId);
        Assert.NotNull(stored.WorkCardId);
        Assert.All(stored.Media, photo => { Assert.NotNull(photo.ArchiveMessageId); Assert.NotNull(photo.WorkMessageId); });
        Assert.All(gateway.Edits, edit => Assert.Null(edit.Card.Buttons));
        Assert.Contains(gateway.Edits, edit => edit.ChatId == stored.ArchiveChatId && edit.Card.Text.Contains("Одобрено"));
        Assert.Equal(4, gateway.Deleted.Count);
        Assert.All(gateway.Deleted, message => Assert.Equal(stored.WorkChatId, message.ChatId));
        Assert.All(await db.DeliveryMessages.Where(x => x.IsWork).ToListAsync(), message => Assert.True(message.Deleted));
        Assert.Single(await db.ScoreEntries.ToListAsync());
    }

    [Fact]
    public async Task Partial_archive_never_allows_board_or_cleanup_and_retry_preserves_all_parts()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, album: true);
        var gateway = new FakeTelegramGateway { NextBatchCount = 1 };
        await DrainAsync(f, gateway);
        await using (var db = f.Open())
        {
            Assert.All(await db.SubmissionMedia.ToListAsync(), photo => Assert.Null(photo.ArchiveMessageId));
            Assert.Single(await db.DeliveryMessages.ToListAsync());
            Assert.Null((await db.Submissions.SingleAsync()).ArchiveCardId);
            Assert.Single(await db.OutgoingActions.Where(x => x.LogicalKey.StartsWith("delivery:")).ToListAsync());
        }
        await f.ExecuteAsync(Moderate(f, submission));
        await DrainAsync(f, gateway);
        Assert.Empty(gateway.Deleted);
        Assert.Empty(gateway.Copies);
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await DrainAsync(f, gateway);
        await using var actual = f.Open();
        Assert.All(await actual.SubmissionMedia.ToListAsync(), photo => Assert.NotNull(photo.ArchiveMessageId));
        Assert.Equal(SubmissionState.Approved, (await actual.Submissions.SingleAsync()).Status);
        Assert.Contains(gateway.Replies, card => card.Text.Contains("Одобрено") && card.Buttons is null);
    }

    [Fact]
    public async Task Partial_work_copy_uses_file_id_fallback_and_cleans_known_partial_messages()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f, album: true);
        var gateway = new FakeTelegramGateway();
        // Candidate prompt (discarded), archive media, archive card.
        for (var i = 0; i < 3; i++) Assert.True(await DeliverAsync(f, gateway));
        gateway.NextBatchCount = 1;
        Assert.True(await DeliverAsync(f, gateway));
        await using (var db = f.Open()) Assert.All(await db.SubmissionMedia.ToListAsync(), media => Assert.Null(media.WorkMessageId));
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await DrainAsync(f, gateway);
        Assert.Equal(2, gateway.Albums.Count);
        Assert.Equal(f.Settings.ModerationChatId, gateway.Albums[1].ChatId);
        await f.ExecuteAsync(Moderate(f, submission));
        await DrainAsync(f, gateway);
        Assert.Equal(5, gateway.Deleted.Count); // partial copy + full album + card
    }

    [Fact]
    public async Task Delete_failure_leaves_terminal_card_without_buttons_and_does_not_undo_score()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        await f.ExecuteAsync(Moderate(f, submission));
        gateway.DeleteFailure = new ApiRequestException("message can't be deleted", 400);
        await DrainAsync(f, gateway);
        Assert.Contains(gateway.Edits, edit => edit.ChatId == f.Settings.ModerationChatId && edit.Card.Buttons is null && edit.Card.Text.Contains("Одобрено"));
        await using var db = f.Open();
        Assert.Single(await db.ScoreEntries.ToListAsync());
        Assert.True((await db.OutgoingActions.SingleAsync(x => x.Kind == DeliveryKind.CleanupWork)).Failed);
        Assert.Contains(await db.OutgoingActions.ToListAsync(), action => action.ChatId == f.Settings.SuperAdminUserId && action.Payload.Contains("CleanupWork"));
    }

    [Fact]
    public async Task Transient_delete_failure_resumes_without_repeating_deleted_messages()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        await f.ExecuteAsync(Moderate(f, submission));
        gateway.DeleteFailure = new IOException("Temporary transport error");
        await DrainAsync(f, gateway);
        Assert.Empty(gateway.Deleted);
        gateway.DeleteFailure = null;
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await DrainAsync(f, gateway);
        Assert.Equal(2, gateway.Deleted.Count);
        await DrainAsync(f, gateway);
        Assert.Equal(2, gateway.Deleted.Count);
    }

    [Fact]
    public async Task Card_delivered_after_decision_is_terminal_without_buttons()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        var gateway = new FakeTelegramGateway();
        for (var i = 0; i < 4; i++) Assert.True(await DeliverAsync(f, gateway)); // work media delivered, card still queued
        await f.ExecuteAsync(Moderate(f, submission));
        Assert.True(await DeliverAsync(f, gateway));
        Assert.Null(gateway.Replies.Last().Buttons);
        Assert.Contains("Одобрено", gateway.Replies.Last().Text);
    }

    [Fact]
    public async Task Replaced_candidate_prompt_is_suppressed_and_current_prompt_replies_to_photo()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        await PhotoAsync(f, 100);
        await PhotoAsync(f, 101);
        var gateway = new FakeTelegramGateway();
        await DrainAsync(f, gateway);
        var prompt = Assert.Single(gateway.Replies);
        Assert.Equal(101, prompt.ReplyToMessageId);
        var candidate = await CandidateAsync(f);
        Assert.Contains(prompt.Buttons!.SelectMany(x => x), button => button.Data == $"confirm:{candidate.Id:N}:{candidate.Version}");
    }

    [Fact]
    public async Task Retry_after_is_respected_and_archive_failure_is_independent_of_decision()
    {
        await using var f = new GameFixture();
        await StartAsync(f);
        var submission = await SubmitAsync(f);
        var gateway = new FakeTelegramGateway
        {
            NextFailure = new ApiRequestException("Too Many Requests", 429, new Telegram.Bot.Types.ResponseParameters { RetryAfter = 20 })
        };
        await DrainAsync(f, gateway);
        await using (var db = f.Open()) Assert.Equal(f.Clock.GetUtcNow().AddSeconds(20), (await db.OutgoingActions.SingleAsync(x => x.Kind == DeliveryKind.ArchiveMedia)).RetryAt);
        f.Clock.Advance(TimeSpan.FromSeconds(19));
        await DrainAsync(f, gateway);
        Assert.Empty(gateway.Albums);
        await f.ExecuteAsync(Moderate(f, submission));
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await DrainAsync(f, gateway);
        Assert.Single(gateway.Albums);
        await using var actual = f.Open();
        Assert.Single(await actual.ScoreEntries.ToListAsync());
    }
}
