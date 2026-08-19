using Zettel.Api.Dto.Placements;

namespace Zettel.Api.Dto.Matches;

public record CreateMatchRequest(
    Guid Fighter1Id,
    Guid? Fighter2Id,
    DateTime? ScheduledAt,
    int? RoundDurationSeconds,
    int? MaxDoubles,
    int? MaxWarnings,
    // Optional: where in the bracket this match belongs. Omitted by manual creation and by
    // clients that predate placements — a match without a cell stays legal (invariant 46).
    MatchPlacementRequest? Placement = null);
