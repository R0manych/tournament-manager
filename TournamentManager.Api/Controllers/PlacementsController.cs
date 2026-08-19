using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Placements;
using TournamentManager.Api.Mapping;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

// Read and repair of bracket placements. There is deliberately no write endpoint: a cell is
// claimed only by POST /tournaments/{id}/matches, in the same transaction as the match it
// holds, so a placement can never point at a match that was never created (docs/08, ОВ-5).
[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/placements")]
public class PlacementsController(TournamentDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid tournamentId, CancellationToken ct)
    {
        var exists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!exists) return NotFound();

        var placements = await db.MatchPlacements
            .AsNoTracking()
            .Where(x => x.TournamentId == tournamentId)
            .OrderBy(x => x.PhaseId).ThenBy(x => x.RoundId).ThenBy(x => x.SlotIndex)
            .ToListAsync(ct);

        return Ok(placements.Select(x => x.ToResponse()).ToList());
    }

    // Frees a cell without touching the match. Cancelling a fight already frees its cell
    // (ОВ-3), so this is for the other case: the match stays live but should no longer be
    // part of the bracket — a fight put in the wrong cell by hand, typically.
    [HttpDelete("{phaseId}/{roundId}/{slotIndex:int}")]
    public async Task<IActionResult> Delete(
        Guid tournamentId, string phaseId, string roundId, int slotIndex, CancellationToken ct)
    {
        var placement = await db.MatchPlacements
            .FirstOrDefaultAsync(
                x => x.TournamentId == tournamentId
                     && x.PhaseId == phaseId
                     && x.RoundId == roundId
                     && x.SlotIndex == slotIndex, ct);

        if (placement is null) return NotFound();

        db.MatchPlacements.Remove(placement);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
