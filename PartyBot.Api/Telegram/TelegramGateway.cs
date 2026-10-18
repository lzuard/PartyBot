using Telegram.Bot;

namespace PartyBot.Api.Telegram;

// Deliberately not registered by bootstrap: no network activity at stage 1.
public sealed class TelegramGateway(ITelegramBotClient client) : ITelegramGateway
{
    public async Task<TelegramMessageReference> SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        var message = await client.SendMessage(chatId, text, cancellationToken: cancellationToken);
        return new TelegramMessageReference(message.Chat.Id, message.Id);
    }
}
