using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Tournaments;
using TournamentManager.Api.Mapping;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Enums;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments")]
public class TournamentsController(TournamentDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] TournamentStatus? status, CancellationToken ct)
    {
        var summaries = await db.Tournaments
            .AsNoTracking()
            .Where(t => status == null || t.Status == status)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TournamentSummaryResponse(
                t.Id, t.Name, t.Description, t.Nomination, t.Location,
                t.StartDate, t.EndDate, t.Status.ToString(),
                t.DefaultRoundDurationSeconds,
                t.DefaultMaxDoubles, t.DefaultMaxWarnings,
                t.CreatedAt,
                t.Participants.Count, t.Matches.Count))
            .ToListAsync(ct);

        return Ok(summaries);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants).ThenInclude(p => p.Fighter)
            .Include(t => t.Matches)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return tournament is null ? NotFound() : Ok(tournament.ToDetailResponse());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTournamentRequest req, CancellationToken ct)
    {
        if (req.EndDate < req.StartDate)
            return Problem("EndDate must be greater than or equal to StartDate.", statusCode: 400);

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            Nomination = req.Nomination,
            Location = req.Location,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Status = TournamentStatus.Draft,
            DefaultRoundDurationSeconds = req.DefaultRoundDurationSeconds,
            DefaultMaxDoubles = req.DefaultMaxDoubles,
            DefaultMaxWarnings = req.DefaultMaxWarnings,
            CreatedAt = DateTime.UtcNow
        };

        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = tournament.Id }, tournament.ToDetailResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CreateTournamentRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([id], ct);
        if (tournament is null) return NotFound();

        if (req.EndDate < req.StartDate)
            return Problem("EndDate must be greater than or equal to StartDate.", statusCode: 400);

        tournament.Name = req.Name;
        tournament.Description = req.Description;
        tournament.Nomination = req.Nomination;
        tournament.Location = req.Location;
        tournament.StartDate = req.StartDate;
        tournament.EndDate = req.EndDate;
        tournament.DefaultRoundDurationSeconds = req.DefaultRoundDurationSeconds;
        tournament.DefaultMaxDoubles = req.DefaultMaxDoubles;
        tournament.DefaultMaxWarnings = req.DefaultMaxWarnings;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([id], ct);
        if (tournament is null) return NotFound();

        db.Tournaments.Remove(tournament);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/participants")]
    public async Task<IActionResult> AddParticipant(Guid id, AddParticipantRequest req, CancellationToken ct)
    {
        var tournamentExists = await db.Tournaments.AnyAsync(t => t.Id == id, ct);
        if (!tournamentExists) return NotFound();

        var fighter = await db.Fighters.FindAsync([req.FighterId], ct);
        if (fighter is null)
            return Problem($"Fighter {req.FighterId} not found.", statusCode: 404);

        var alreadyRegistered = await db.TournamentParticipants
            .AnyAsync(p => p.TournamentId == id && p.FighterId == req.FighterId, ct);
        if (alreadyRegistered)
            return Problem("Fighter is already registered in this tournament.", statusCode: 409);

        var participant = new TournamentParticipant
        {
            TournamentId = id,
            FighterId = req.FighterId,
            Seed = req.Seed,
            RegisteredAt = DateTime.UtcNow
        };

        db.TournamentParticipants.Add(participant);
        await db.SaveChangesAsync(ct);

        var response = new ParticipantResponse(
            fighter.Id, fighter.FirstName, fighter.LastName,
            fighter.Club, req.Seed, participant.RegisteredAt);

        return Created($"api/v1/tournaments/{id}/participants/{fighter.Id}", response);
    }

    [HttpDelete("{id:guid}/participants/{fighterId:guid}")]
    public async Task<IActionResult> RemoveParticipant(Guid id, Guid fighterId, CancellationToken ct)
    {
        var participant = await db.TournamentParticipants
            .FirstOrDefaultAsync(p => p.TournamentId == id && p.FighterId == fighterId, ct);

        if (participant is null) return NotFound();

        db.TournamentParticipants.Remove(participant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, UpdateTournamentStatusRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<TournamentStatus>(req.Status, out var newStatus))
            return Problem($"Invalid status value: '{req.Status}'.", statusCode: 400);

        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tournament is null) return NotFound();

        var (valid, error) = IsValidTransition(tournament.Status, newStatus);
        if (!valid) return Problem(error, statusCode: 409);

        tournament.Status = newStatus;
        await db.SaveChangesAsync(ct);

        return Ok(tournament.ToDetailResponse());
    }

    private static (bool valid, string error) IsValidTransition(TournamentStatus from, TournamentStatus to) =>
        (from, to) switch
        {
            (TournamentStatus.Draft, TournamentStatus.Active) => (true, ""),
            (TournamentStatus.Draft, TournamentStatus.Cancelled) => (true, ""),
            (TournamentStatus.Active, TournamentStatus.Completed) => (true, ""),
            (TournamentStatus.Active, TournamentStatus.Cancelled) => (true, ""),
            (TournamentStatus.Completed, TournamentStatus.Active) => (true, ""),
            (TournamentStatus.Cancelled, TournamentStatus.Draft) => (true, ""),
            _ => (false, $"Cannot transition from {from} to {to}.")
        };
}
