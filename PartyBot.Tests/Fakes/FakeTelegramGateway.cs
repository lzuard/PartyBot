using PartyBot.Api.Application;
using PartyBot.Api.Telegram;

namespace PartyBot.Tests.Fakes;

public sealed class FakeTelegramGateway : ITelegramGateway, IMediaTelegramGateway
{
    private int nextId;
    public List<(long ChatId, IReadOnlyList<PhotoReference> Photos)> Albums { get; } = [];
    public List<(long ChatId, int MessageId, BotReply Card)> Edits { get; } = [];
    public List<TelegramMessageReference> Deleted { get; } = [];
    public List<(long ChatId, long SourceChatId, IReadOnlyList<int> Ids)> Copies { get; } = [];
    public int? NextBatchCount { get; set; }
    public Exception? DeleteFailure { get; set; }
    public Action<TelegramMessageReference>? BeforeDelete { get; set; }
    public Action? BeforeNetwork { get; set; }
    public List<(TelegramMessageReference Reference, string Text)> Sent { get; } = [];
    public List<BotReply> Replies { get; } = [];
    public Exception? NextFailure { get; set; }

    public async Task<TelegramMessageReference> SendReplyAsync(long chatId, BotReply reply,
        CancellationToken cancellationToken = default)
    {
        var reference = await SendTextAsync(chatId, reply.Text, cancellationToken);
        Replies.Add(reply);
        return reference;
    }

    public Task<TelegramMessageReference> SendTextAsync(long chatId, string text,
        CancellationToken cancellationToken = default)
    {
        BeforeNetwork?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }

        var reference = new TelegramMessageReference(chatId, ++nextId);
        Sent.Add((reference, text));
        return Task.FromResult(reference);
    }
    private void CheckFailure()
    {
        BeforeNetwork?.Invoke();
        if (NextFailure is { } failure) { NextFailure = null; throw failure; }
    }
    private IReadOnlyList<TelegramMessageReference> Batch(long chatId, int count)
    {
        count = NextBatchCount ?? count;
        NextBatchCount = null;
        return Enumerable.Range(0, count).Select(_ => new TelegramMessageReference(chatId, ++nextId)).ToArray();
    }
    public Task<IReadOnlyList<TelegramMessageReference>> SendPhotosAsync(long chatId, IReadOnlyList<PhotoReference> photos, CancellationToken ct)
    {
        CheckFailure();
        Albums.Add((chatId, photos));
        return Task.FromResult(Batch(chatId, photos.Count));
    }
    public Task<IReadOnlyList<TelegramMessageReference>> CopyPhotosAsync(long chatId, long sourceChatId, IReadOnlyList<int> ids, CancellationToken ct)
    {
        CheckFailure();
        Copies.Add((chatId, sourceChatId, ids));
        return Task.FromResult(Batch(chatId, ids.Count));
    }
    public async Task<IReadOnlyList<TelegramMessageReference>> SendCardAsync(long chatId, string? longDetails, BotReply card, CancellationToken ct)
    {
        var sent = new List<TelegramMessageReference>();
        if (longDetails is not null) sent.Add(await SendTextAsync(chatId, longDetails, ct));
        sent.Add(await SendReplyAsync(chatId, card, ct));
        return sent;
    }
    public Task EditCardAsync(long chatId, int messageId, BotReply card, CancellationToken ct)
    {
        CheckFailure();
        Edits.Add((chatId, messageId, card));
        return Task.CompletedTask;
    }
    public Task DeleteAsync(long chatId, int messageId, CancellationToken ct)
    {
        CheckFailure();
        BeforeDelete?.Invoke(new(chatId, messageId));
        if (DeleteFailure is { } failure) throw failure;
        Deleted.Add(new(chatId, messageId));
        return Task.CompletedTask;
    }

}