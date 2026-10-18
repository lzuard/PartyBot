using PartyBot.Api.Application;

namespace PartyBot.Api.Telegram;

public sealed record PhotoReference(string FileId, string? Caption);

public interface IMediaTelegramGateway
{
    Task<IReadOnlyList<TelegramMessageReference>> SendPhotosAsync(long chatId, IReadOnlyList<PhotoReference> photos, CancellationToken ct);
    Task<IReadOnlyList<TelegramMessageReference>> CopyPhotosAsync(long chatId, long sourceChatId, IReadOnlyList<int> messageIds, CancellationToken ct);
    Task<IReadOnlyList<TelegramMessageReference>> SendCardAsync(long chatId, string? longDetails, BotReply card, CancellationToken ct);
    Task EditCardAsync(long chatId, int messageId, BotReply card, CancellationToken ct);
    Task DeleteAsync(long chatId, int messageId, CancellationToken ct);
}
