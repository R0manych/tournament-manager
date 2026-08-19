using TournamentManager.Api.Dto.Matches;
using TournamentManager.Api.Dto.Placements;
using TournamentManager.Domain.Entities;

namespace TournamentManager.Api.Mapping;

public static class MatchMappingExtensions
{
    public static MatchResponse ToResponse(this Match m, Tournament? t = null, MatchPlacement? placement = null) => new(
        m.Id, m.TournamentId,
        m.Team1Id ?? m.Fighter1Id,
        m.Team2Id ?? m.Fighter2Id,
        m.ScheduledAt, m.Status.ToString(),
        m.Score1, m.Score2, m.Warnings1, m.Warnings2,
        m.Exchanges.Count(e => e.IsDoubleHit),
        m.WinnerId,
        m.RoundDurationSeconds, m.MaxDoubles, m.MaxWarnings,
        m.RoundDurationSeconds
            ?? m.Encounter?.BoutDurationSeconds
            ?? t?.DefaultRoundDurationSeconds,
        m.MaxDoubles ?? t?.DefaultMaxDoubles,
        m.MaxWarnings ?? t?.DefaultMaxWarnings,
        m.EncounterId, m.BoutNumber, m.TargetCumulativeScore,
        m.StartedAt, m.CurrentRoundNumber, m.CurrentRoundStartedAt,
        m.EndedAt, m.CreatedAt,
        m.Exchanges.OrderBy(e => e.Sequence).Select(e => e.ToResponse()).ToList(),
        placement?.ToResponse());

    public static MatchPlacementResponse ToResponse(this MatchPlacement p) =>
        new(p.PhaseId, p.RoundId, p.SlotIndex, p.MatchId);

    public static ExchangeResponse ToResponse(this Exchange e) => new(
        e.Id, e.Sequence, e.RoundNumber,
        e.Points1, e.Points2, e.IsDoubleHit, e.Note, e.CreatedAt);
}
