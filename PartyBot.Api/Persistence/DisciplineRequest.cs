namespace PartyBot.Api.Persistence;

public sealed class DisciplineRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ActorId { get; set; }
    public int ParticipantId { get; set; }
    public int ParticipantVersion { get; set; }
    public int GameVersion { get; set; }
    public bool Disqualify { get; set; }
    public string Reason { get; set; } = "";
    public bool Active { get; set; } = true;
}
