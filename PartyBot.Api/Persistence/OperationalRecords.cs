namespace PartyBot.Api.Persistence;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; }
    public long? ActorId { get; set; }
    public int? ParticipantId { get; set; }
    public string? TaskId { get; set; }
    public Guid? AttemptId { get; set; }
    public string Action { get; set; } = "";
    public string? Data { get; set; }
}

public sealed class ProcessedUpdate
{
    public int Id { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class PollingState
{
    public int Id { get; set; } = 1;
    public int? NextUpdateId { get; set; }
}

// Only text replies at stage 2; media delivery/dependencies are implemented in stage 3.
public sealed class OutgoingAction
{
    public long Id { get; set; }
    public string LogicalKey { get; set; } = "";
    public long ChatId { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset RetryAt { get; set; }
    public int Attempts { get; set; }
    public int? MessageId { get; set; }
    public bool Failed { get; set; }
}
