namespace TournamentManager.Domain.Entities;

// Where a match sits in the bracket: phase → round → cell. Kept out of Match on purpose
// (invariant 23: Match has no Stage), by the same convention as TournamentGroup — the
// backend stores the placement, the frontend computes the bracket. See docs/08.
//
// PhaseId and RoundId are not foreign keys: phases and rounds live inside Tournament.Format,
// they have no tables of their own.
public class MatchPlacement
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string PhaseId { get; set; } = null!;
    public string RoundId { get; set; } = null!;
    public int SlotIndex { get; set; }
    public Guid MatchId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public Match Match { get; set; } = null!;
}
