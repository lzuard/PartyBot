namespace PartyBot.Api.Domain;

public enum CandidateState { Collecting, Ready, Confirmed, Replaced, Cancelled }
public enum SubmissionState { Pending, Approved, Rejected, Unreviewed }

public sealed class AnswerCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public int ParticipantId { get; set; }
    public string? MediaGroupId { get; set; }
    public CandidateState State { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset LastPartAt { get; set; }
    public DateTimeOffset ReadyAt { get; set; }
    public List<CandidateMedia> Media { get; set; } = [];
}

public sealed class CandidateMedia
{
    public long Id { get; set; }
    public Guid CandidateId { get; set; }
    public long SourceChatId { get; set; }
    public int SourceMessageId { get; set; }
    public string FileId { get; set; } = "";
    public string FileUniqueId { get; set; } = "";
    public string? Caption { get; set; }
}

public sealed class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public int ParticipantId { get; set; }
    public string TaskId { get; set; } = "";
    public string AuthorSnapshot { get; set; } = "";
    public string TextSnapshot { get; set; } = "";
    public int PointsSnapshot { get; set; }
    public bool RepeatableSnapshot { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public SubmissionState Status { get; set; }
    public int Version { get; set; }
    public long? ModeratorId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RejectReason { get; set; }
    public long ArchiveChatId { get; set; }
    public long WorkChatId { get; set; }
    public int? ArchiveCardId { get; set; }
    public int? WorkCardId { get; set; }
    public List<SubmissionMedia> Media { get; set; } = [];
}

public sealed class SubmissionMedia
{
    public Guid SubmissionId { get; set; }
    public int Order { get; set; }
    public string FileId { get; set; } = "";
    public string FileUniqueId { get; set; } = "";
    public long SourceChatId { get; set; }
    public int SourceMessageId { get; set; }
    public string? CaptionSnapshot { get; set; }
    public int? ArchiveMessageId { get; set; }
    public int? WorkMessageId { get; set; }
}

public sealed class ScoreEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public int ParticipantId { get; set; }
    public int Points { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RejectChoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public int SubmissionVersion { get; set; }
    public long ActorId { get; set; }
    public bool Active { get; set; } = true;
}

// Includes partial batches: no Telegram message with a known ID is silently lost.
public sealed class DeliveryMessage
{
    public long Id { get; set; }
    public Guid SubmissionId { get; set; }
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public bool IsWork { get; set; }
    public bool Deleted { get; set; }
}
