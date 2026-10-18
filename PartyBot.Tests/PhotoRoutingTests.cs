using PartyBot.Api.Application;
using PartyBot.Api.Telegram;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PartyBot.Tests;

public sealed class PhotoRoutingTests
{
    [Fact]
    public void Photo_uses_largest_size_and_retains_caption_and_group_but_edits_are_ignored()
    {
        var message = new Message
        {
            Id = 123, Chat = new Chat { Id = 100, Type = ChatType.Private }, From = new User { Id = 100, FirstName = "Test" },
            Caption = "caption", MediaGroupId = "group", Photo =
            [new PhotoSize { FileId = "small", FileUniqueId = "us", Width = 10, Height = 10 },
             new PhotoSize { FileId = "large", FileUniqueId = "ul", Width = 100, Height = 100 }]
        };
        var command = UpdateRouter.Map(new Update { Id = 1, Message = message });
        Assert.Equal(CommandKind.Photo, command.Kind);
        Assert.Equal(new IncomingPhoto(123, "large", "ul", "group", "caption"), command.Photo);
        Assert.Equal(CommandKind.Ignore, UpdateRouter.Map(new Update { Id = 2, EditedMessage = message }).Kind);
        message.Photo = null;
        message.Video = new Video { FileId = "video", FileUniqueId = "uv", Width = 10, Height = 10, Duration = 1 };
        var unsupported = UpdateRouter.Map(new Update { Id = 3, Message = message });
        Assert.Equal(CommandKind.UnsupportedMedia, unsupported.Kind);
        Assert.Equal("group", unsupported.UnsupportedGroupId);
    }

    [Theory]
    [InlineData("confirm:bad:1")]
    [InlineData("confirm:00000000000000000000000000000000:not-version")]
    [InlineData("approve:00000000000000000000000000000000")]
    [InlineData("rejectcancel:bad")]
    public void Malformed_photo_or_moderation_callbacks_are_ignored(string data)
    {
        var update = new Update { Id = 1, CallbackQuery = new CallbackQuery
        {
            Id = "c", ChatInstance = "chat", From = new User { Id = 998, FirstName = "Test" }, Data = data,
            Message = new Message { Id = 1, Chat = new Chat { Id = -1, Type = ChatType.Supergroup } }
        }};
        Assert.Equal(CommandKind.Ignore, UpdateRouter.Map(update).Kind);
    }
}
