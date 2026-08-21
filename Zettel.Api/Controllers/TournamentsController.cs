using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zettel.Api.Dto.Tournaments;
using Zettel.Api.Mapping;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Controllers;

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
                t.ParticipantKind.ToString(),
                t.DefaultRoundDurationSeconds,
                t.DefaultMaxDoubles, t.DefaultMaxWarnings,
                t.DefaultTeamTargetScore, t.DefaultTeamBoutDurationSeconds,
                t.CreatedAt,
                t.Participants.Count, t.Matches.Count))
            .ToListAsync(ct);

        return Ok(summaries);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tournament is null) return NotFound();

        var lookup = await BuildParticipantLookupAsync(tournament, ct);
        return Ok(tournament.ToDetailResponse(lookup));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTournamentRequest req, CancellationToken ct)
    {
        if (req.EndDate < req.StartDate)
            return Problem("EndDate must be greater than or equal to StartDate.", statusCode: 400);

        if (!TryParseParticipantKind(req.ParticipantKind, ParticipantKind.Fighter, out var kind, out var kindError))
            return Problem(kindError, statusCode: 400);

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
            ParticipantKind = kind,
            DefaultRoundDurationSeconds = req.DefaultRoundDurationSeconds,
            DefaultMaxDoubles = req.DefaultMaxDoubles,
            DefaultMaxWarnings = req.DefaultMaxWarnings,
            DefaultTeamTargetScore = req.DefaultTeamTargetScore,
            DefaultTeamBoutDurationSeconds = req.DefaultTeamBoutDurationSeconds,
            CreatedAt = DateTime.UtcNow
        };

        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = tournament.Id }, tournament.ToDetailResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CreateTournamentRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tournament is null) return NotFound();

        if (req.EndDate < req.StartDate)
            return Problem("EndDate must be greater than or equal to StartDate.", statusCode: 400);

        if (!TryParseParticipantKind(req.ParticipantKind, tournament.ParticipantKind, out var kind, out var kindError))
            return Problem(kindError, statusCode: 400);

        if (kind != tournament.ParticipantKind && tournament.Participants.Count > 0)
            return Problem(
                "ParticipantKind cannot be changed after participants are registered.",
                statusCode: 409);

        tournament.Name = req.Name;
        tournament.Description = req.Description;
        tournament.Nomination = req.Nomination;
        tournament.Location = req.Location;
        tournament.StartDate = req.StartDate;
        tournament.EndDate = req.EndDate;
        tournament.ParticipantKind = kind;
        tournament.DefaultRoundDurationSeconds = req.DefaultRoundDurationSeconds;
        tournament.DefaultMaxDoubles = req.DefaultMaxDoubles;
        tournament.DefaultMaxWarnings = req.DefaultMaxWarnings;
        tournament.DefaultTeamTargetScore = req.DefaultTeamTargetScore;
        tournament.DefaultTeamBoutDurationSeconds = req.DefaultTeamBoutDurationSeconds;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static bool TryParseParticipantKind(
        string? raw, ParticipantKind fallback,
        out ParticipantKind parsed, out string error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = fallback;
            error = "";
            return true;
        }

        if (Enum.TryParse(raw, ignoreCase: true, out parsed))
        {
            error = "";
            return true;
        }

        error = $"Invalid participantKind value: '{raw}'. Expected 'Fighter' or 'Team'.";
        return false;
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
        var tournament = await db.Tournaments.FindAsync([id], ct);
        if (tournament is null) return NotFound();

        var alreadyRegistered = await db.TournamentParticipants
            .AnyAsync(p => p.TournamentId == id && p.ParticipantId == req.ParticipantId, ct);
        if (alreadyRegistered)
            return Problem("Participant is already registered in this tournament.", statusCode: 409);

        ParticipantResponse response;
        if (tournament.ParticipantKind == ParticipantKind.Fighter)
        {
            var fighter = await db.Fighters.FindAsync([req.ParticipantId], ct);
            if (fighter is null)
                return Problem($"Fighter {req.ParticipantId} not found.", statusCode: 404);

            response = new ParticipantResponse(
                fighter.Id, ParticipantKind.Fighter.ToString(),
                new FighterParticipantInfo(fighter.FirstName, fighter.LastName, fighter.Club),
                null, req.Seed, DateTime.UtcNow);
        }
        else
        {
            var team = await db.Teams.FirstOrDefaultAsync(
                x => x.Id == req.ParticipantId && x.TournamentId == id, ct);
            if (team is null)
                return Problem(
                    $"Team {req.ParticipantId} not found in this tournament.",
                    statusCode: 404);

            response = new ParticipantResponse(
                team.Id, ParticipantKind.Team.ToString(),
                null,
                new TeamParticipantInfo(team.Name, team.Club, team.City),
                req.Seed, DateTime.UtcNow);
        }

        var participant = new TournamentParticipant
        {
            TournamentId = id,
            ParticipantId = req.ParticipantId,
            Seed = req.Seed,
            RegisteredAt = response.RegisteredAt
        };

        db.TournamentParticipants.Add(participant);
        await db.SaveChangesAsync(ct);

        // Absolute path — see AddMember in TeamsController. The participant is
        // addressable for DELETE and read through the tournament.
        return Created(
            $"/api/v1/tournaments/{id}/participants/{req.ParticipantId}",
            response);
    }

    [HttpDelete("{id:guid}/participants/{participantId:guid}")]
    public async Task<IActionResult> RemoveParticipant(Guid id, Guid participantId, CancellationToken ct)
    {
        var participant = await db.TournamentParticipants
            .FirstOrDefaultAsync(p => p.TournamentId == id && p.ParticipantId == participantId, ct);

        if (participant is null) return NotFound();

        db.TournamentParticipants.Remove(participant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ParticipantLookup> BuildParticipantLookupAsync(
        Tournament t, CancellationToken ct)
    {
        var lookup = new ParticipantLookup();
        if (t.Participants.Count == 0) return lookup;

        var ids = t.Participants.Select(p => p.ParticipantId).ToList();
        if (t.ParticipantKind == ParticipantKind.Fighter)
        {
            var fighters = await db.Fighters
                .AsNoTracking()
                .Where(f => ids.Contains(f.Id))
                .ToListAsync(ct);
            foreach (var f in fighters) lookup.Fighters[f.Id] = f;
        }
        else
        {
            var teams = await db.Teams
                .AsNoTracking()
                .Where(team => team.TournamentId == t.Id && ids.Contains(team.Id))
                .ToListAsync(ct);
            foreach (var team in teams) lookup.Teams[team.Id] = team;
        }
        return lookup;
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

        // Rollback to Draft deletes generated fights so groups become editable
        // again. From Scheduled only unstarted fights may exist; from Active this
        // is the admin-confirmed "reset fights" — results are deleted as well.
        if (newStatus == TournamentStatus.Draft
            && tournament.Status is TournamentStatus.Scheduled or TournamentStatus.Active)
        {
            if (tournament.Status == TournamentStatus.Scheduled)
            {
                // DoubleLoss counts as a recorded result even when the fight never started
                // (mutual no-show): the rollback would delete it silently.
                var started = tournament.Matches.Any(m =>
                    m.Status is MatchStatus.InProgress or MatchStatus.Completed or MatchStatus.DoubleLoss);
                if (started)
                    return Problem(
                        "Some fights have already started; use the reset from Active status.",
                        statusCode: 409);
            }

            var encounters = await db.Encounters
                .Where(e => e.TournamentId == id)
                .ToListAsync(ct);
            db.Encounters.RemoveRange(encounters);
            db.Matches.RemoveRange(tournament.Matches);
        }

        tournament.Status = newStatus;
        await db.SaveChangesAsync(ct);

        var lookup = await BuildParticipantLookupAsync(tournament, ct);
        return Ok(tournament.ToDetailResponse(lookup));
    }

    private static (bool valid, string error) IsValidTransition(TournamentStatus from, TournamentStatus to) =>
        (from, to) switch
        {
            (TournamentStatus.Draft, TournamentStatus.Scheduled) => (true, ""),
            (TournamentStatus.Draft, TournamentStatus.Active) => (true, ""),
            (TournamentStatus.Draft, TournamentStatus.Cancelled) => (true, ""),
            (TournamentStatus.Scheduled, TournamentStatus.Active) => (true, ""),
            (TournamentStatus.Scheduled, TournamentStatus.Draft) => (true, ""),
            (TournamentStatus.Scheduled, TournamentStatus.Cancelled) => (true, ""),
            (TournamentStatus.Active, TournamentStatus.Completed) => (true, ""),
            (TournamentStatus.Active, TournamentStatus.Draft) => (true, ""),
            (TournamentStatus.Active, TournamentStatus.Cancelled) => (true, ""),
            (TournamentStatus.Completed, TournamentStatus.Active) => (true, ""),
            // Cancelled is terminal. The restore that used to live here (Cancelled → Draft)
            // did not delete the generated matches the way the other rollbacks do, so the
            // tournament came back "in setup" with fights from its previous life — and with
            // the freeze keyed on Status != Draft that also unlocked the format and groups.
            // Reviving a cancelled tournament now means creating a new one.
            (TournamentStatus.Cancelled, _) =>
                (false, "Tournament is cancelled; this is a terminal status and cannot be changed."),
            _ => (false, $"Cannot transition from {from} to {to}.")
        };
}
