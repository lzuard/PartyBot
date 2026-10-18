namespace PartyBot.Api.Telegram;

public readonly record struct TelegramMessageReference(long ChatId, int MessageId);

// Minimal adapter seam; media and polling are added with their use cases.
public interface ITelegramGateway
{
    Task<TelegramMessageReference> SendTextAsync(long chatId, string text, CancellationToken cancellationToken = default);
}
