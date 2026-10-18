using PartyBot.Api.Application;
using BotCommand = PartyBot.Api.Application.BotCommand;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PartyBot.Api.Telegram;

public static class UpdateRouter
{
    public static BotCommand Map(Update update)
    {
        if (update.CallbackQuery is { Message: { } message, Data: { } data } callback)
        {
            var parts = data.Split(':');
            var kind = parts[0] switch
            {
                "get" => CommandKind.GetTask, "show" => CommandKind.ShowTask,
                "skip" => CommandKind.AskSkip, "skipyes" => CommandKind.ConfirmSkip, "skipno" => CommandKind.CancelSkip,
                "openyes" => CommandKind.ConfirmOpen, "openno" => CommandKind.CancelOpen,
                _ => CommandKind.Help
            };
            Guid? attemptId = null;
            int? version = null;
            if (kind is CommandKind.AskSkip or CommandKind.ConfirmSkip or CommandKind.CancelSkip)
            {
                if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "N", out var parsed)) kind = CommandKind.Help;
                else attemptId = parsed;
            }
            if (kind is CommandKind.ConfirmOpen or CommandKind.CancelOpen)
            {
                if (parts.Length != 2 || !int.TryParse(parts[1], out var parsed)) kind = CommandKind.Help;
                else version = parsed;
            }
            return new(update.Id, callback.From.Id, message.Chat.Id, message.Chat.Type == ChatType.Private && !callback.From.IsBot,
                kind, AttemptId: attemptId, GameVersion: version);
        }
        if (update.Message is { From: { IsBot: false } from, Text: { } text } input)
        {
            var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts.FirstOrDefault()?.Split('@')[0].ToLowerInvariant();
            var kind = command switch
            {
                "/start" => CommandKind.Start, "/name" => CommandKind.Name,
                "/task" => CommandKind.GetTask, "/mytask" => CommandKind.ShowTask,
                "/skip" => CommandKind.AskSkip, "/open" => CommandKind.AskOpen,
                _ => CommandKind.Help
            };
            return new(update.Id, from.Id, input.Chat.Id, input.Chat.Type == ChatType.Private, kind,
                Name: parts.Length == 2 ? parts[1] : null);
        }
        return new(update.Id, 0, 0, false, CommandKind.Ignore);
    }
}
