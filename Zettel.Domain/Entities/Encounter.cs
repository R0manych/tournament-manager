using Zettel.Domain.Enums;

namespace Zettel.Domain.Entities;

public class Encounter
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid Participant1Id { get; set; }
    public Guid Participant2Id { get; set; }

    public DateTime? ScheduledAt { get; set; }
    public MatchStatus Status { get; set; }

    public int TargetTotalScore { get; set; }
    public int BoutDurationSeconds { get; set; }

    public Guid? WinnerParticipantId { get; set; }
    public Guid? PriorityParticipantId { get; set; }

    // Серия занимает площадку целиком, от первого боута до тай-брейка; боуты наследуют
    // её через Match.EffectivePisteId (docs/09 §3.2).
    public Guid? PisteId { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public Team Participant1 { get; set; } = null!;
    public Team Participant2 { get; set; } = null!;
    public Piste? Piste { get; set; }
    public List<Match> Bouts { get; set; } = new();
}
