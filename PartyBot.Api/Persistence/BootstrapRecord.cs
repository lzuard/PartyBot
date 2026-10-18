namespace PartyBot.Api.Persistence;

// Infrastructure marker only. Game entities arrive with their implementation stages.
public sealed class BootstrapRecord
{
    public int Id { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}