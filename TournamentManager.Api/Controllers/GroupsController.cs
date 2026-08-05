using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Groups;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Enums;
using TournamentManager.Domain.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class GroupsController(TournamentDbContext db) : ControllerBase
{
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

    // Replaces the saved composition of one phase's groups (create and manual edit
    // go through the same endpoint). Editing groups that already have generated
    // matches is allowed — the client is responsible for warning the user.
    [HttpPut("tournaments/{tournamentId:guid}/groups")]
    public async Task<IActionResult> Save(Guid tournamentId, SaveGroupsRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Groups.Where(g => g.PhaseId == req.PhaseId))
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is null) return NotFound();

        // Groups are only editable during setup; after match generation the
        // tournament moves to Scheduled and requires a confirmed rollback to Draft.
        if (tournament.Status != TournamentStatus.Draft)
            return Problem(
                $"Groups can only be edited while the tournament is in Draft (current: {tournament.Status}). " +
                "Roll the tournament back to Draft first.",
                statusCode: 409);

        if (tournament.Format is null)
            return Problem("Tournament has no format uploaded.", statusCode: 400);

        var phase = tournament.Format.Phases.OfType<RoundRobinPhase>()
            .FirstOrDefault(p => p.Id == req.PhaseId);
        if (phase is null)
            return Problem($"Round-robin phase '{req.PhaseId}' not found in format.", statusCode: 400);

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
            PhaseId = req.PhaseId,
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
