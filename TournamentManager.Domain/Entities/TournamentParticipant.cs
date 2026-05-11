namespace TournamentManager.Domain.Entities;

public class TournamentParticipant
{
    public Guid TournamentId { get; set; }
    public Guid FighterId { get; set; }
    public int? Seed { get; set; }
    public DateTime RegisteredAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public Fighter Fighter { get; set; } = null!;
}
