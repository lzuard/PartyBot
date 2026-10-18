using PartyBot.Api.Application;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PartyBot.Api.Telegram;

public sealed class TelegramGateway(ITelegramBotClient client) : ITelegramGateway, ITelegramUpdateSource
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
            message = await client.SendMessage(chatId, text[..length], replyMarkup: keyboard,
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
}