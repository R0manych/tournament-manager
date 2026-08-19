using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Placements;
using TournamentManager.Domain.Entities;
using TournamentManager.Infrastructure.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Common;

// Validation of a bracket cell before a match is put into it.
//
// Scope is deliberate (docs/08, ОВ-2): the phase and the round are checked strictly, the
// slot index only for being non-negative. Checking that the index fits the round would mean
// porting the round-size arithmetic — trivial for singleElimination, but for doubleElimination
// it is computeUBMatchCounts with entersAt byes and dropdowns — and that is a separate task.
// An index past the end of a round is caught in practice by the unique cell index: two
// matches still cannot share a cell, and the bracket simply will not draw a cell that the
// format does not describe.
public static class PlacementGuard
{
    public static async Task<string?> ValidateAsync(
        TournamentDbContext db, Tournament tournament, MatchPlacementRequest placement, CancellationToken ct)
    {
        if (tournament.Format is null)
            return "Tournament has no format uploaded; a match cannot be placed in a bracket.";

        if (placement.SlotIndex < 0)
            return $"slotIndex must be >= 0 (got {placement.SlotIndex}).";

        // Only consulted for roundRobin phases, where the group labels are the round ids.
        var groupLabels = await db.TournamentGroups
            .AsNoTracking()
            .Where(g => g.TournamentId == tournament.Id && g.PhaseId == placement.PhaseId)
            .Select(g => g.Label)
            .ToListAsync(ct);

        var roundIds = FormatRoundCatalog.RoundIdsOf(tournament.Format, placement.PhaseId, groupLabels);

        if (roundIds is null)
            return $"Phase '{placement.PhaseId}' not found in the tournament format.";

        if (!roundIds.Contains(placement.RoundId))
            return $"Round '{placement.RoundId}' does not exist in phase '{placement.PhaseId}'. " +
                   $"Known rounds: {string.Join(", ", roundIds.OrderBy(x => x, StringComparer.Ordinal))}.";

        return null;
    }
}
