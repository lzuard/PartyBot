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
    Help
}

public sealed record BotCommand(
    int UpdateId,
    long ActorId,
    long ChatId,
    bool IsPrivate,
    CommandKind Kind,
    string? Name = null,
    Guid? AttemptId = null,
    int? GameVersion = null);

public sealed record BotButton(string Text, string Data);

public sealed record BotReply(string Text, BotButton[][]? Buttons = null);

// One process, one business command at a time, including future game-state transitions.
public sealed class CommandGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public void Dispose()
    {
        Semaphore.Dispose();
    }
}