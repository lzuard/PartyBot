namespace PartyBot.Api.Persistence;

public sealed class RecoveryRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ActorId { get; set; }
    public long ActionId { get; set; }
    public int Attempts { get; set; }
    public bool Active { get; set; } = true;
}
