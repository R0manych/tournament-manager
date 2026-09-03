using Zettel.Api.Dto.Matches;
using Zettel.Api.Dto.Placements;
using Zettel.Domain.Entities;
using Zettel.Infrastructure.Format;

namespace Zettel.Api.Mapping;

public static class MatchMappingExtensions
{
    public static MatchResponse ToResponse(this Match m, Tournament? t = null, MatchPlacement? placement = null)
    {
        // Per-round `overrides` of the format sit between the values set on the fight itself
        // and the tournament-wide defaults: a round is more specific than the tournament and
        // less specific than a single fight (ТЗ §5.3). They are reachable only for a placed
        // match — the placement is what says which round the fight belongs to, so a caller
        // that drops the placement from its response also reports different effective
        // settings than GET /matches/{id} would.
        var ov = placement is null
            ? null
            : FormatRoundCatalog.OverrideFor(t?.Format, placement.PhaseId, placement.RoundId);

        return new(
            m.Id, m.TournamentId,
            m.Team1Id ?? m.Fighter1Id,
            m.Team2Id ?? m.Fighter2Id,
            m.ScheduledAt, m.Status.ToString(),
            m.Score1, m.Score2, m.Warnings1, m.Warnings2,
            m.VideoReplays1, m.VideoReplays2,
            m.Exchanges.Count(e => e.IsDoubleHit),
            m.WinnerId,
            m.RoundDurationSeconds, m.MaxDoubles, m.MaxWarnings,
            // A bout keeps its series duration ahead of the round override: bouts belong to an
            // Encounter, and an Encounter is never placed in a cell (B-8), so the two cannot
            // collide in practice.
            m.RoundDurationSeconds
                ?? m.Encounter?.BoutDurationSeconds
                ?? ov?.RoundDurationSeconds
                ?? t?.DefaultRoundDurationSeconds,
            m.MaxDoubles ?? ov?.MaxDoubles ?? t?.DefaultMaxDoubles,
            m.MaxWarnings ?? ov?.MaxWarnings ?? t?.DefaultMaxWarnings,
            m.EncounterId, m.BoutNumber, m.TargetCumulativeScore,
            // Имя — всегда у эффективного ристалища: подпись на табло и в списке относится к
            // площадке, где бой идёт, а у боута это площадка его серии (docs/09 §3.2). Оно
            // едет вместе с id, чтобы табло не делало второй запрос ради подписи; если
            // навигация не подгружена, остаётся null — id при этом верен.
            m.PisteId, m.EffectivePisteId,
            m.PisteId is not null ? m.Piste?.Name : m.Encounter?.Piste?.Name,
            m.StartedAt, m.CurrentRoundNumber, m.CurrentRoundStartedAt,
            m.EndedAt, m.CreatedAt,
            m.Exchanges.OrderBy(e => e.Sequence).Select(e => e.ToResponse()).ToList(),
            placement?.ToResponse());
    }

    public static MatchPlacementResponse ToResponse(this MatchPlacement p) =>
        new(p.PhaseId, p.RoundId, p.SlotIndex, p.MatchId);

    public static ExchangeResponse ToResponse(this Exchange e) => new(
        e.Id, e.Sequence, e.RoundNumber,
        e.Points1, e.Points2, e.IsDoubleHit, e.Note, e.CreatedAt);
}
