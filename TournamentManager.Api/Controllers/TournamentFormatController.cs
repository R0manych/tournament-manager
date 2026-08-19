using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Common;
using TournamentManager.Domain.Format;
using TournamentManager.Infrastructure.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/format")]
public class TournamentFormatController(TournamentDbContext db, ITournamentFormatParser parser) : ControllerBase
{
    private const string ForceHint =
        "Pass ?force=true to replace it anyway; saved group compositions and bracket " +
        "placements of phases that are missing from the new format are discarded, and " +
        "already generated matches are left as they are — they may no longer match the " +
        "new bracket.";

    // force=true is the organiser's explicit "I accept the consequences": the format
    // changes under matches that were generated from the old one. Everything the new
    // format no longer describes is dropped rather than left dangling (B-2).
    [HttpPut]
    public async Task<IActionResult> UploadFormat(Guid tournamentId, [FromQuery] bool force, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();

        if (TournamentSetupGuard.IsLocked(tournament) && !force)
            return Conflict(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-frozen",
                Title = "Tournament format is frozen",
                Status = 409,
                Detail = TournamentSetupGuard.LockedDetail(tournament, "change the format", ForceHint),
            });

        string yaml;
        if (Request.ContentType?.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
        {
            var file = Request.Form.Files.GetFile("file");
            if (file == null)
                return BadRequest(new ProblemDetails { Title = "Missing file", Detail = "Provide a file field in multipart/form-data." });
            using var reader = new StreamReader(file.OpenReadStream());
            yaml = await reader.ReadToEndAsync(ct);
        }
        else
        {
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            yaml = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(yaml))
            return BadRequest(new ProblemDetails { Title = "Empty body", Detail = "YAML body must not be empty." });

        var result = parser.Parse(yaml);
        if (!result.IsValid)
        {
            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-invalid",
                Title = "Tournament format is invalid",
                Status = 400,
            };
            problem.Extensions["errors"] = result.Errors;
            return new ObjectResult(problem) { StatusCode = 400 };
        }

        // Only a forced replace prunes groups: without force the tournament is still in
        // Draft, where the organiser is expected to fix the composition themselves.
        var cleared = 0;
        var placementsCleared = 0;
        if (force)
        {
            var survivingGroupPhases = result.Format!.Phases
                .OfType<RoundRobinPhase>()
                .Select(p => p.Id)
                .ToHashSet();

            var orphaned = await db.TournamentGroups
                .Where(g => g.TournamentId == tournamentId && !survivingGroupPhases.Contains(g.PhaseId))
                .ToListAsync(ct);

            db.TournamentGroups.RemoveRange(orphaned);
            cleared = orphaned.Count;

            // Placements live in every kind of phase, not just round-robin, so they are
            // pruned against the full phase list (docs/08, invariant 45). Matches themselves
            // survive — dropping them is what the rollback to Draft is for.
            var survivingPhases = result.Format.Phases.Select(p => p.Id).ToHashSet();

            var orphanedPlacements = await db.MatchPlacements
                .Where(x => x.TournamentId == tournamentId && !survivingPhases.Contains(x.PhaseId))
                .ToListAsync(ct);

            db.MatchPlacements.RemoveRange(orphanedPlacements);
            placementsCleared = orphanedPlacements.Count;
        }

        tournament.Format = result.Format;
        tournament.FormatYaml = yaml;
        await db.SaveChangesAsync(ct);

        // Body stays the parsed format — the counts ride along in headers so the
        // client can invalidate its groups and bracket caches without a contract change.
        Response.Headers["X-Groups-Cleared"] = cleared.ToString();
        Response.Headers["X-Placements-Cleared"] = placementsCleared.ToString();
        return Ok(result.Format);
    }

    [HttpGet]
    public async Task<IActionResult> GetFormat(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();
        if (tournament.Format == null) return NotFound(new ProblemDetails { Detail = "No format uploaded for this tournament." });

        return Ok(tournament.Format);
    }

    [HttpGet("raw")]
    public async Task<IActionResult> GetFormatRaw(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();
        if (string.IsNullOrEmpty(tournament.FormatYaml)) return NotFound(new ProblemDetails { Detail = "No format uploaded for this tournament." });

        return Content(tournament.FormatYaml, "text/yaml");
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteFormat(Guid tournamentId, [FromQuery] bool force, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();

        if (TournamentSetupGuard.IsLocked(tournament) && !force)
            return Conflict(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-frozen",
                Title = "Tournament format is frozen",
                Status = 409,
                Detail = TournamentSetupGuard.LockedDetail(tournament, "delete the format", ForceHint),
            });

        // Without a format no phase exists, so every saved group and every placement is orphaned.
        var cleared = 0;
        var placementsCleared = 0;
        if (force)
        {
            var groups = await db.TournamentGroups
                .Where(g => g.TournamentId == tournamentId)
                .ToListAsync(ct);

            db.TournamentGroups.RemoveRange(groups);
            cleared = groups.Count;

            var placements = await db.MatchPlacements
                .Where(x => x.TournamentId == tournamentId)
                .ToListAsync(ct);

            db.MatchPlacements.RemoveRange(placements);
            placementsCleared = placements.Count;
        }

        tournament.Format = null;
        tournament.FormatYaml = null;
        await db.SaveChangesAsync(ct);

        Response.Headers["X-Groups-Cleared"] = cleared.ToString();
        Response.Headers["X-Placements-Cleared"] = placementsCleared.ToString();
        return NoContent();
    }
}
