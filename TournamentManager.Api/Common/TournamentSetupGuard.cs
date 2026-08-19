using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Enums;

namespace TournamentManager.Api.Common;

// Single definition of "the tournament has already started" for everything that is
// only editable while it is being set up: the uploaded format and the saved group
// composition. Both used to answer this question differently — the format looked at
// Matches.Count, the groups at Status — which made "started" mean two things (B-2).
//
// Draft is the one editable state. The honest way back is the rollback in
// PATCH /tournaments/{id}/status, which deletes the generated matches and encounters,
// so unfreezing never leaves a bracket half-synced with the format. The force flag
// below is the escape hatch for the cases the rollback does not cover.
public static class TournamentSetupGuard
{
    public static bool IsLocked(Tournament tournament) =>
        tournament.Status != TournamentStatus.Draft;

    // Shared wording so the client sees the same explanation whichever resource it hit.
    public static string LockedDetail(Tournament tournament, string action, string? forceHint = null)
    {
        var detail =
            $"Tournament status is {tournament.Status}; this is only editable while the tournament is in Draft. " +
            $"Roll it back to Draft (PATCH /tournaments/{{id}}/status) before you {action}.";
        return forceHint is null ? detail : $"{detail} {forceHint}";
    }
}
