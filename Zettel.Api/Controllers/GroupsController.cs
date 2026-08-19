using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zettel.Api.Common;
using Zettel.Api.Dto.Groups;
using Zettel.Domain.Entities;
using Zettel.Domain.Format;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class GroupsController(TournamentDbContext db) : ControllerBase
{
    // Read-only summary across every phase of the tournament. The editable
    // resource is the per-phase collection below — this one is deliberately
    // GET-only, since a tournament-wide replace has no meaningful semantics.
    [HttpGet("tournaments/{tournamentId:guid}/groups")]
    public async Task<IActionResult> GetAll(Guid tournamentId, CancellationToken ct)
    {
        var exists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!exists) return NotFound();

        var groups = await db.TournamentGroups
            .AsNoTracking()
            .Where(g => g.TournamentId == tournamentId)
            .OrderBy(g => g.PhaseId).ThenBy(g => g.OrderIndex)
            .ToListAsync(ct);

        return Ok(groups.Select(ToResponse).ToList());
    }

    // The exact resource PUT below replaces — same URI, same representation.
    [HttpGet("tournaments/{tournamentId:guid}/phases/{phaseId}/groups")]
    public async Task<IActionResult> GetByPhase(Guid tournamentId, string phaseId, CancellationToken ct)
    {
        var exists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!exists) return NotFound();

        var groups = await db.TournamentGroups
            .AsNoTracking()
            .Where(g => g.TournamentId == tournamentId && g.PhaseId == phaseId)
            .OrderBy(g => g.OrderIndex)
            .ToListAsync(ct);

        return Ok(groups.Select(ToResponse).ToList());
    }

    // Replaces the saved composition of one phase's groups (create and manual edit
    // go through the same endpoint). Editing groups that already have generated
    // matches is allowed — the client is responsible for warning the user.
    [HttpPut("tournaments/{tournamentId:guid}/phases/{phaseId}/groups")]
    public async Task<IActionResult> Save(
        Guid tournamentId, string phaseId, SaveGroupsRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Groups.Where(g => g.PhaseId == phaseId))
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is null) return NotFound();

        if (req.PhaseId is not null && req.PhaseId != phaseId)
            return Problem(
                $"phaseId in the body ('{req.PhaseId}') does not match the route ('{phaseId}'). " +
                "The route is authoritative; omit the field from the body.",
                statusCode: 400);

        // Groups are only editable during setup; after match generation the tournament
        // moves to Scheduled and requires a confirmed rollback to Draft. Same criterion
        // as the format freeze — see TournamentSetupGuard.
        if (TournamentSetupGuard.IsLocked(tournament))
            return Problem(
                TournamentSetupGuard.LockedDetail(tournament, "edit the group composition"),
                title: "Tournament setup is frozen",
                statusCode: 409);

        if (tournament.Format is null)
            return Problem("Tournament has no format uploaded.", statusCode: 400);

        var phase = tournament.Format.Phases.OfType<RoundRobinPhase>()
            .FirstOrDefault(p => p.Id == phaseId);
        if (phase is null)
            return Problem($"Round-robin phase '{phaseId}' not found in format.", statusCode: 400);

        if (req.Groups is null || req.Groups.Count == 0)
            return Problem("At least one group is required.", statusCode: 400);

        var labels = req.Groups.Select(g => g.Label).ToList();
        if (labels.Any(string.IsNullOrWhiteSpace))
            return Problem("Group label must not be empty.", statusCode: 400);
        if (labels.Distinct().Count() != labels.Count)
            return Problem("Group labels must be unique.", statusCode: 400);

        var registeredIds = tournament.Participants.Select(p => p.ParticipantId).ToHashSet();
        var allIds = req.Groups.SelectMany(g => g.ParticipantIds).ToList();
        var unknown = allIds.Where(pid => !registeredIds.Contains(pid)).ToList();
        if (unknown.Count > 0)
            return Problem(
                $"Participants not registered in this tournament: {string.Join(", ", unknown)}.",
                statusCode: 400);
        if (allIds.Distinct().Count() != allIds.Count)
            return Problem("Participant appears in more than one group or twice in the same group.", statusCode: 400);

        db.TournamentGroups.RemoveRange(tournament.Groups);

        var now = DateTime.UtcNow;
        var saved = req.Groups.Select((g, i) => new TournamentGroup
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            PhaseId = phaseId,
            Label = g.Label,
            OrderIndex = i,
            ParticipantIds = g.ParticipantIds,
            UpdatedAt = now,
        }).ToList();

        db.TournamentGroups.AddRange(saved);
        await db.SaveChangesAsync(ct);

        return Ok(saved.Select(ToResponse).ToList());
    }

    private static GroupResponse ToResponse(TournamentGroup g) =>
        new(g.PhaseId, g.Label, g.ParticipantIds, g.UpdatedAt);
}
