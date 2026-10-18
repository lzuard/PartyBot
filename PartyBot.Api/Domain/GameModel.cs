namespace PartyBot.Api.Domain;

public enum GameState { Registration, Open, Review, Finished }
public enum AttemptState { Active, Skipped, Submitted }
public enum TaskLocation { Pool, Queue, Active, Pending, Consumed }

public sealed class Game
{
    public int Id { get; set; } = 1;
    public GameState State { get; set; }
    public int Version { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public int? SkipCooldownMinutes { get; set; }
    public int? SubmitCooldownMinutes { get; set; }
    public bool DemonstrationCatalog { get; set; }
}

public sealed class TaskDefinition
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public int Points { get; set; } = 1;
    public bool Repeatable { get; set; }
}

public sealed class Participant
{
    public int Id { get; set; }
    public long TelegramUserId { get; set; }
    public long PrivateChatId { get; set; }
    public string DisplayName { get; set; } = "";
    public bool Disqualified { get; set; }
    public DateTimeOffset? NextTaskAvailableAt { get; set; }
    public int NextBucket { get; set; }
    public long NextPosition { get; set; }
    public int Version { get; set; }
    public List<TaskAvailability> Tasks { get; set; } = [];
}

public sealed class TaskAvailability
{
    public int ParticipantId { get; set; }
    public Participant Participant { get; set; } = null!;
    public string TaskId { get; set; } = "";
    public TaskDefinition Task { get; set; } = null!;
    public TaskLocation Location { get; set; }
    public long Position { get; set; }
    public Guid? CurrentAttemptId { get; set; }
    public Attempt? CurrentAttempt { get; set; }
}

public sealed class Attempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ParticipantId { get; set; }
    public string TaskId { get; set; } = "";
    public AttemptState State { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
