namespace PartyBot.Api.Application;

public enum CommandKind
{
    Ignore,
    Start,
    Name,
    GetTask,
    ShowTask,
    AskSkip,
    ConfirmSkip,
    CancelSkip,
    AskOpen,
    ConfirmOpen,
    CancelOpen,
    Diagnostics, AskRetryDelivery, ConfirmRetryDelivery, CancelRetryDelivery,
    AskClose, AskFinalize, AskForceFinalize, ConfirmTransition, CancelTransition,
    Leaderboard, Admin, AdminParticipant, AskDisqualify, AskReinstate, ConfirmDiscipline, CancelDiscipline,
    Help, UnsupportedMedia, Photo, ConfirmAnswer, ReplaceAnswer, Approve, AskReject, ConfirmReject, CancelReject
}

public sealed record BotCommand(
    int UpdateId,
    long ActorId,
    long ChatId,
    bool IsPrivate,
    CommandKind Kind,
    string? Name = null,
    Guid? AttemptId = null,
    int? GameVersion = null,
    Guid? EntityId = null,
    int? EntityVersion = null,
    string? ReasonId = null,
    IncomingPhoto? Photo = null, string? UnsupportedGroupId = null, int? ParticipantId = null, int Page = 0);

public sealed record IncomingPhoto(int MessageId, string FileId, string FileUniqueId, string? MediaGroupId = null, string? Caption = null);

public sealed record BotButton(string Text, string Data);

public sealed record BotReply(string Text, BotButton[][]? Buttons = null, int? ReplyToMessageId = null, int? MessageThreadId = null);

// One process, one business command at a time, including future game-state transitions.
public sealed class CommandGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public void Dispose()
    {
        Semaphore.Dispose();
    }
}