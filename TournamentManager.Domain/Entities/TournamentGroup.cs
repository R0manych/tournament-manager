namespace TournamentManager.Domain.Entities;

// Persisted group composition of a round-robin phase. ParticipantIds is ordered
// (seed order within the group) and polymorphic: Fighter.Id or Team.Id depending
// on Tournament.ParticipantKind — same convention as TournamentParticipant.
public class TournamentGroup
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string PhaseId { get; set; } = null!;
    public string Label { get; set; } = null!;
    public int OrderIndex { get; set; }
    public List<Guid> ParticipantIds { get; set; } = new();
    public DateTime UpdatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
