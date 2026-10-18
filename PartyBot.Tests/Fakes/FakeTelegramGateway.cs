using PartyBot.Api.Application;
using PartyBot.Api.Telegram;

namespace PartyBot.Tests.Fakes;

public sealed class FakeTelegramGateway : ITelegramGateway
{
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
        cancellationToken.ThrowIfCancellationRequested();
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }

        var reference = new TelegramMessageReference(chatId, Sent.Count + 1);
        Sent.Add((reference, text));
        return Task.FromResult(reference);
    }
}