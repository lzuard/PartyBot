using PartyBot.Api.Application;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PartyBot.Api.Telegram;

public sealed class TelegramGateway(ITelegramBotClient client) : ITelegramGateway, ITelegramUpdateSource, IMediaTelegramGateway
{
    public Task<TelegramMessageReference> SendTextAsync(long chatId, string text,
        CancellationToken cancellationToken = default)
    {
        return SendReplyAsync(chatId, new BotReply(text), cancellationToken);
    }

    public async Task<TelegramMessageReference> SendReplyAsync(long chatId, BotReply reply,
        CancellationToken cancellationToken = default)
    {
        var text = reply.Text;
        Message message;
        do
        {
            var length = Math.Min(4000, text.Length);
            if (length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
            var last = length == text.Length;
            var keyboard = last && reply.Buttons is { } buttons
                ? new InlineKeyboardMarkup(buttons.Select(row =>
                    row.Select(button => InlineKeyboardButton.WithCallbackData(button.Text, button.Data))))
                : null;
            // Plain text (no parse mode): participant names and catalog text cannot inject markup.
            message = await client.SendMessage(chatId, text[..length], replyMarkup: keyboard, messageThreadId: reply.MessageThreadId,
                replyParameters: reply.ReplyToMessageId is { } sourceId ? new ReplyParameters { MessageId = sourceId, AllowSendingWithoutReply = true } : null,
                cancellationToken: cancellationToken);
            text = text[length..];
        } while (text.Length > 0);

        return new TelegramMessageReference(message.Chat.Id, message.Id);
    }

    public Task<Update[]> FetchAsync(int? offset, CancellationToken cancellationToken)
    {
        return client.GetUpdates(
            offset, timeout: 30, allowedUpdates: [UpdateType.Message, UpdateType.CallbackQuery],
            cancellationToken: cancellationToken);
    }

    public Task AcknowledgeAsync(string callbackId, CancellationToken cancellationToken)
    {
        return client.AnswerCallbackQuery(callbackId, cancellationToken: cancellationToken);
    }
    public async Task<IReadOnlyList<TelegramMessageReference>> SendPhotosAsync(long chatId, IReadOnlyList<PhotoReference> photos, CancellationToken ct)
    {
        if (photos.Count == 1)
        {
            var message = await client.SendPhoto(chatId, InputFile.FromFileId(photos[0].FileId), caption: photos[0].Caption, cancellationToken: ct);
            return [new(chatId, message.Id)];
        }
        var messages = await client.SendMediaGroup(chatId,
            photos.Select(photo => new InputMediaPhoto(InputFile.FromFileId(photo.FileId)) { Caption = photo.Caption }), cancellationToken: ct);
        return messages.Select(message => new TelegramMessageReference(chatId, message.Id)).ToArray();
    }

    public async Task<IReadOnlyList<TelegramMessageReference>> CopyPhotosAsync(long chatId, long sourceChatId, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        var messages = await client.CopyMessages(chatId, sourceChatId, messageIds, cancellationToken: ct);
        return messages.Select(message => new TelegramMessageReference(chatId, message.Id)).ToArray();
    }

    public async Task<IReadOnlyList<TelegramMessageReference>> SendCardAsync(long chatId, string? longDetails, BotReply card, CancellationToken ct)
    {
        var sent = new List<TelegramMessageReference>();
        while (!string.IsNullOrEmpty(longDetails))
        {
            var length = Math.Min(4000, longDetails.Length);
            if (length < longDetails.Length && char.IsHighSurrogate(longDetails[length - 1])) length--;
            sent.Add(await SendTextAsync(chatId, longDetails[..length], ct));
            longDetails = longDetails[length..];
        }
        sent.Add(await SendReplyAsync(chatId, card, ct));
        return sent;
    }

    public async Task EditCardAsync(long chatId, int messageId, BotReply card, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(card.Buttons?.Select(row => row.Select(button =>
            InlineKeyboardButton.WithCallbackData(button.Text, button.Data))) ?? []);
        try { await client.EditMessageText(chatId, messageId, card.Text, replyMarkup: keyboard, cancellationToken: ct); }
        catch (global::Telegram.Bot.Exceptions.ApiRequestException error) when (error.ErrorCode == 400 && error.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase)) { }
    }

    public async Task DeleteAsync(long chatId, int messageId, CancellationToken ct)
    {
        try { await client.DeleteMessage(chatId, messageId, cancellationToken: ct); }
        catch (global::Telegram.Bot.Exceptions.ApiRequestException error) when (error.ErrorCode == 400 && error.Message.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase)) { }
    }

}