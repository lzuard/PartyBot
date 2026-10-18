using PartyBot.Api.Application;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using BotCommand = PartyBot.Api.Application.BotCommand;

namespace PartyBot.Api.Telegram;

public static class UpdateRouter
{
    public static BotCommand Map(Update update)
    {
        if (update.CallbackQuery is { Message: { } message, Data: { } data } callback)
        {
            if (callback.From.IsBot) return new BotCommand(update.Id, 0, 0, false, CommandKind.Ignore);
            var parts = data.Split(':');
            var photoKind = parts[0] switch
            {
                "confirm" => CommandKind.ConfirmAnswer, "replace" => CommandKind.ReplaceAnswer,
                "approve" => CommandKind.Approve, "rejectask" => CommandKind.AskReject,
                "reject" => CommandKind.ConfirmReject, "rejectcancel" => CommandKind.CancelReject,
                _ => CommandKind.Ignore
            };
            if (photoKind != CommandKind.Ignore)
            {
                var hasVersion = photoKind is not (CommandKind.ConfirmReject or CommandKind.CancelReject);
                var expectedLength = photoKind == CommandKind.CancelReject ? 2 : 3;
                if (parts.Length != expectedLength || !Guid.TryParseExact(parts[1], "N", out var entityId) ||
                    (hasVersion && !int.TryParse(parts[2], out _)))
                    return new BotCommand(update.Id, 0, 0, false, CommandKind.Ignore);
                return new BotCommand(update.Id, callback.From.Id, message.Chat.Id, message.Chat.Type == ChatType.Private,
                    photoKind, EntityId: entityId, EntityVersion: hasVersion ? int.Parse(parts[2]) : null,
                    ReasonId: photoKind == CommandKind.ConfirmReject ? parts[2] : null);
            }

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

            return new BotCommand(update.Id, callback.From.Id, message.Chat.Id,
                message.Chat.Type == ChatType.Private && !callback.From.IsBot,
                kind, AttemptId: attemptId, GameVersion: version);
        }

        if (update.Message is { From: { IsBot: false } sender, Photo: { Length: > 0 } sizes } photo)
        {
            var largest = sizes.MaxBy(size => (long)size.Width * size.Height)!;
            return new BotCommand(update.Id, sender.Id, photo.Chat.Id, photo.Chat.Type == ChatType.Private, CommandKind.Photo,
                Photo: new IncomingPhoto(photo.Id, largest.FileId, largest.FileUniqueId, photo.MediaGroupId, photo.Caption));
        }
        if (update.Message is { From: { IsBot: false } mediaSender } unsupported &&
            (unsupported.Video is not null || unsupported.Document is not null || unsupported.Animation is not null || unsupported.Audio is not null || unsupported.Voice is not null))
            return new BotCommand(update.Id, mediaSender.Id, unsupported.Chat.Id, unsupported.Chat.Type == ChatType.Private,
                CommandKind.UnsupportedMedia, UnsupportedGroupId: unsupported.MediaGroupId);
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
            return new BotCommand(update.Id, from.Id, input.Chat.Id, input.Chat.Type == ChatType.Private, kind,
                parts.Length == 2 ? parts[1] : null);
        }

        return new BotCommand(update.Id, 0, 0, false, CommandKind.Ignore);
    }
}