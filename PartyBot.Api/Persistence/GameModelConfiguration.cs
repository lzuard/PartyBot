using Microsoft.EntityFrameworkCore;
using PartyBot.Api.Domain;

namespace PartyBot.Api.Persistence;

internal static class GameModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Game>(entity =>
        {
            entity.ToTable("Games", table => table.HasCheckConstraint("CK_Game_Singleton", "Id = 1"));
            entity.Property(game => game.Id).ValueGeneratedNever();
            entity.Property(game => game.Version).IsConcurrencyToken();
        });
        model.Entity<TaskDefinition>(entity =>
        {
            entity.ToTable("TaskDefinitions", table => table.HasCheckConstraint("CK_Task_Points", "Points > 0"));
            entity.Property(task => task.Id).ValueGeneratedNever();
        });
        model.Entity<Participant>(entity =>
        {
            entity.HasIndex(participant => participant.TelegramUserId).IsUnique();
            entity.HasIndex(participant => participant.PrivateChatId).IsUnique();
            entity.Property(participant => participant.Version).IsConcurrencyToken();
        });
        model.Entity<Attempt>(entity =>
        {
            entity.HasIndex(attempt => attempt.ParticipantId).IsUnique().HasFilter("State = 0");
            entity.HasOne<Participant>().WithMany().HasForeignKey(attempt => attempt.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaskDefinition>().WithMany().HasForeignKey(attempt => attempt.TaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(attempt => new { attempt.Id, attempt.ParticipantId, attempt.TaskId });
        });
        model.Entity<TaskAvailability>(entity =>
        {
            entity.HasKey(task => new { task.ParticipantId, task.TaskId });
            entity.HasOne(task => task.Participant).WithMany(participant => participant.Tasks)
                .HasForeignKey(task => task.ParticipantId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.Task).WithMany().HasForeignKey(task => task.TaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.CurrentAttempt).WithMany()
                .HasForeignKey(task => new { task.CurrentAttemptId, task.ParticipantId, task.TaskId })
                .HasPrincipalKey(attempt => new { attempt.Id, attempt.ParticipantId, attempt.TaskId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(task => task.ParticipantId).IsUnique().HasFilter("Location = 2");
            entity.ToTable("TaskAvailabilities", table => table.HasCheckConstraint("CK_Availability_Attempt",
                "(Location IN (2, 3) AND CurrentAttemptId IS NOT NULL) OR (Location IN (0, 1, 4) AND CurrentAttemptId IS NULL)"));
        });
        model.Entity<AuditEvent>().HasIndex(item => item.At);
        model.Entity<ProcessedUpdate>().Property(update => update.Id).ValueGeneratedNever();
        model.Entity<PollingState>(entity =>
        {
            entity.Property(state => state.Id).ValueGeneratedNever();
            entity.ToTable("PollingStates", table => table.HasCheckConstraint("CK_Polling_Singleton", "Id = 1"));
        });
        model.Entity<OutgoingAction>().HasIndex(action => action.LogicalKey).IsUnique();
        model.Entity<OutgoingAction>().HasIndex(action => new { action.Failed, action.MessageId, action.RetryAt });
    }
}
