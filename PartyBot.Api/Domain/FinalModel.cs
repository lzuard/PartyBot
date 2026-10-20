namespace PartyBot.Api.Domain;

public enum TransitionKind { CloseIntake, Finalize, ForceFinalize }

public sealed class TransitionRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ActorId { get; set; }
    public int GameVersion { get; set; }
    public TransitionKind Kind { get; set; }
    public int PendingCount { get; set; }
    public string? Reason { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class FinalStanding
{
    public int GameId { get; set; } = 1;
    public int ParticipantId { get; set; }
    public long TelegramUserId { get; set; }
    public string Name { get; set; } = "";
    public bool Disqualified { get; set; }
    public int? Rank { get; set; }
    public long Points { get; set; }
    public DateTimeOffset? LastApprovedSubmittedAt { get; set; }
}
