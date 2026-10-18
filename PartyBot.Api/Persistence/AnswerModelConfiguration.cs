using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Domain;

namespace PartyBot.Api.Persistence;

internal static class AnswerModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AnswerCandidate>(entity =>
        {
            entity.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.AttemptId).IsUnique().HasFilter("State IN (0, 1)");
            entity.HasIndex(x => new { x.ParticipantId, x.MediaGroupId }).IsUnique().HasFilter("MediaGroupId IS NOT NULL");
            entity.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<CandidateMedia>(entity =>
        {
            entity.HasOne<AnswerCandidate>().WithMany(x => x.Media).HasForeignKey(x => x.CandidateId);
            entity.HasIndex(x => new { x.SourceChatId, x.SourceMessageId }).IsUnique();
        });
        model.Entity<Submission>(entity =>
        {
            entity.HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaskDefinition>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.AttemptId).IsUnique();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.ToTable("Submissions", table => table.HasCheckConstraint("CK_Submission_Points", "PointsSnapshot > 0"));
        });
        model.Entity<SubmissionMedia>(entity =>
        {
            entity.HasKey(x => new { x.SubmissionId, x.Order });
            entity.HasOne<Submission>().WithMany(x => x.Media).HasForeignKey(x => x.SubmissionId);
            entity.HasIndex(x => new { x.SourceChatId, x.SourceMessageId }).IsUnique();
        });
        model.Entity<ScoreEntry>(entity =>
        {
            entity.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.SubmissionId).IsUnique();
            entity.ToTable("ScoreEntries", table => table.HasCheckConstraint("CK_Score_Positive", "Points > 0"));
        });
        model.Entity<RejectChoice>().HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId);
        model.Entity<DeliveryMessage>(entity =>
        {
            entity.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId);
            entity.HasIndex(x => new { x.ChatId, x.MessageId }).IsUnique();
        });
    }
}
