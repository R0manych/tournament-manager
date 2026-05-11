using TournamentManager.Api.Dto.Matches;
using TournamentManager.Domain.Entities;

namespace TournamentManager.Api.Mapping;

public static class MatchMappingExtensions
{
    public static MatchResponse ToResponse(this Match m, Tournament? t = null) => new(
        m.Id, m.TournamentId, m.Fighter1Id, m.Fighter2Id,
        m.ScheduledAt, m.Status.ToString(),
        m.Score1, m.Score2, m.Warnings1, m.Warnings2,
        m.Exchanges.Count(e => e.IsDoubleHit),
        m.WinnerId,
        m.RoundDurationSeconds, m.TotalRounds, m.MaxDoubles, m.MaxWarnings,
        m.RoundDurationSeconds ?? t?.DefaultRoundDurationSeconds,
        m.TotalRounds ?? t?.DefaultRoundsPerMatch,
        m.MaxDoubles ?? t?.DefaultMaxDoubles,
        m.MaxWarnings ?? t?.DefaultMaxWarnings,
        m.StartedAt, m.CurrentRoundNumber, m.CurrentRoundStartedAt,
        m.EndedAt, m.CreatedAt,
        m.Exchanges.OrderBy(e => e.Sequence).Select(e => e.ToResponse()).ToList());

    public static ExchangeResponse ToResponse(this Exchange e) => new(
        e.Id, e.Sequence, e.RoundNumber,
        e.Points1, e.Points2, e.IsDoubleHit, e.Note, e.CreatedAt);
}
