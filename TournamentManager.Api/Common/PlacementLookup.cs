using Microsoft.EntityFrameworkCore;
using TournamentManager.Domain.Entities;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Common;

// Every endpoint that answers with a MatchResponse needs the match's cell, not only the ones
// that are about the bracket: the cell names the round, and the round decides which
// `overrides` block of the format applies to the fight (ТЗ §5.3). An endpoint that skips the
// lookup answers with the tournament default where GET /matches/{id} answers with the
// overridden value — the two responses then disagree about the length of the same round.
public static class PlacementLookup
{
    public static Task<MatchPlacement?> PlacementOfAsync(
        this TournamentDbContext db, Guid matchId, CancellationToken ct) =>
        db.MatchPlacements.AsNoTracking().FirstOrDefaultAsync(x => x.MatchId == matchId, ct);
}
