using PartyBot.Api.Application;
using Telegram.Bot.Types;

namespace PartyBot.Api.Telegram;

public readonly record struct TelegramMessageReference(long ChatId, int MessageId);

public interface ITelegramGateway
{
    Task<TelegramMessageReference> SendTextAsync(long chatId, string text,
        CancellationToken cancellationToken = default);

    Task<TelegramMessageReference> SendReplyAsync(long chatId, BotReply reply,
        CancellationToken cancellationToken = default);
}

public interface ITelegramUpdateSource
{
    Task<Update[]> FetchAsync(int? offset, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string callbackId, CancellationToken cancellationToken);
}