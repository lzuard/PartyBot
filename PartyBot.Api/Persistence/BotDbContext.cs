using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PartyBot.Api.Domain;

namespace PartyBot.Api.Persistence;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<BootstrapRecord> BootstrapRecords => Set<BootstrapRecord>();

    public DbSet<DisciplineRequest> DisciplineRequests => Set<DisciplineRequest>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<TaskDefinition> TaskDefinitions => Set<TaskDefinition>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<TaskAvailability> TaskAvailabilities => Set<TaskAvailability>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ProcessedUpdate> ProcessedUpdates => Set<ProcessedUpdate>();
    public DbSet<PollingState> PollingStates => Set<PollingState>();
    public DbSet<OutgoingAction> OutgoingActions => Set<OutgoingAction>();

    public DbSet<AnswerCandidate> AnswerCandidates => Set<AnswerCandidate>();
    public DbSet<CandidateMedia> CandidateMedia => Set<CandidateMedia>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionMedia> SubmissionMedia => Set<SubmissionMedia>();
    public DbSet<ScoreEntry> ScoreEntries => Set<ScoreEntry>();
    public DbSet<RejectChoice> RejectChoices => Set<RejectChoice>();
    public DbSet<DeliveryMessage> DeliveryMessages => Set<DeliveryMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DisciplineRequest>().HasOne<Participant>().WithMany().HasForeignKey(r => r.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        GameModelConfiguration.Configure(modelBuilder);
        AnswerModelConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<BootstrapRecord>(entity =>
        {
            entity.ToTable("Bootstrap", table => table.HasCheckConstraint("CK_Bootstrap_Singleton", "Id = 1"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedNever();
        });
    }
}

// INTEGER UTC ticks preserve chronological SQL comparison/order, including offsets.
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    value => new DateTimeOffset(value, TimeSpan.Zero));