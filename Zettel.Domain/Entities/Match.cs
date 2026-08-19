using Zettel.Domain.Enums;

namespace Zettel.Domain.Entities;

public class Match
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public Guid? Fighter1Id { get; set; }
    public Guid? Fighter2Id { get; set; }
    public Guid? Team1Id { get; set; }
    public Guid? Team2Id { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public MatchStatus Status { get; set; }

    public int Score1 { get; set; }
    public int Score2 { get; set; }
    public int Warnings1 { get; set; }
    public int Warnings2 { get; set; }
    public Guid? WinnerId { get; set; }

    public int? RoundDurationSeconds { get; set; }
    public int? MaxDoubles { get; set; }
    public int? MaxWarnings { get; set; }

    public DateTime? StartedAt { get; set; }
    public int CurrentRoundNumber { get; set; } = 1;
    public DateTime? CurrentRoundStartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Guid? EncounterId { get; set; }
    public int? BoutNumber { get; set; }
    public int? TargetCumulativeScore { get; set; }

    public bool IsTeamMatch => Team1Id.HasValue && Team2Id.HasValue;

    public Tournament Tournament { get; set; } = null!;
    public Fighter? Fighter1 { get; set; }
    public Fighter? Fighter2 { get; set; }
    public Team? Team1 { get; set; }
    public Team? Team2 { get; set; }
    public Encounter? Encounter { get; set; }
    public List<Exchange> Exchanges { get; set; } = new();
}
