namespace TournamentManager.Domain.Entities;

public class TournamentParticipant
{
    public Guid TournamentId { get; set; }
    public Guid ParticipantId { get; set; }
    public int? Seed { get; set; }
    public DateTime RegisteredAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
