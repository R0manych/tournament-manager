using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Teams;
using TournamentManager.Api.Mapping;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Enums;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class TeamsController(TournamentDbContext db) : ControllerBase
{
    [HttpGet("tournaments/{tournamentId:guid}/teams")]
    public async Task<IActionResult> GetByTournament(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();
        if (tournament.ParticipantKind != ParticipantKind.Team)
            return Problem("Tournament is not a team tournament.", statusCode: 409);

        var teams = await db.Teams
            .Include(t => t.Members).ThenInclude(m => m.Fighter)
            .AsNoTracking()
            .Where(t => t.TournamentId == tournamentId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return Ok(teams.Select(t => t.ToResponse()).ToList());
    }

    [HttpGet("teams/{teamId:guid}")]
    public async Task<IActionResult> GetById(Guid teamId, CancellationToken ct)
    {
        var team = await db.Teams
            .Include(t => t.Members).ThenInclude(m => m.Fighter)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teamId, ct);

        return team is null ? NotFound() : Ok(team.ToResponse());
    }

    [HttpPost("tournaments/{tournamentId:guid}/teams")]
    public async Task<IActionResult> Create(
        Guid tournamentId, CreateTeamRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();
        if (tournament.ParticipantKind != ParticipantKind.Team)
            return Problem("Tournament is not a team tournament.", statusCode: 409);

        var nameTaken = await db.Teams
            .AnyAsync(t => t.TournamentId == tournamentId && t.Name == req.Name, ct);
        if (nameTaken)
            return Problem($"Team '{req.Name}' already exists in this tournament.", statusCode: 409);

        var team = new Team
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Name = req.Name,
            Club = req.Club,
            City = req.City,
            CreatedAt = DateTime.UtcNow,
        };

        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { teamId = team.Id }, team.ToResponse());
    }

    [HttpPut("teams/{teamId:guid}")]
    public async Task<IActionResult> Update(
        Guid teamId, CreateTeamRequest req, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([teamId], ct);
        if (team is null) return NotFound();

        if (team.Name != req.Name)
        {
            var nameTaken = await db.Teams
                .AnyAsync(t => t.TournamentId == team.TournamentId
                            && t.Id != teamId
                            && t.Name == req.Name, ct);
            if (nameTaken)
                return Problem($"Team '{req.Name}' already exists in this tournament.", statusCode: 409);
        }

        team.Name = req.Name;
        team.Club = req.Club;
        team.City = req.City;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("teams/{teamId:guid}")]
    public async Task<IActionResult> Delete(Guid teamId, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([teamId], ct);
        if (team is null) return NotFound();

        var hasEncounters = await db.Encounters
            .AnyAsync(e => e.Participant1Id == teamId || e.Participant2Id == teamId, ct);
        if (hasEncounters)
            return Problem(
                "Team is referenced by encounters; cancel or delete them first.",
                statusCode: 409);

        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("teams/{teamId:guid}/members")]
    public async Task<IActionResult> AddMember(
        Guid teamId, AddTeamMemberRequest req, CancellationToken ct)
    {
        var team = await db.Teams.FindAsync([teamId], ct);
        if (team is null) return NotFound();

        var fighter = await db.Fighters.FindAsync([req.FighterId], ct);
        if (fighter is null)
            return Problem($"Fighter {req.FighterId} not found.", statusCode: 404);

        var alreadyInTeam = await db.TeamMembers
            .AnyAsync(tm => tm.TeamId == teamId && tm.FighterId == req.FighterId, ct);
        if (alreadyInTeam)
            return Problem("Fighter is already in this team.", statusCode: 409);

        var positionTaken = await db.TeamMembers
            .AnyAsync(tm => tm.TeamId == teamId && tm.Position == req.Position, ct);
        if (positionTaken)
            return Problem($"Position {req.Position} is already taken in this team.", statusCode: 409);

        // Fighter cannot be in another team of the same tournament.
        var inAnotherTeam = await db.TeamMembers
            .AnyAsync(tm => tm.FighterId == req.FighterId
                         && tm.Team.TournamentId == team.TournamentId
                         && tm.TeamId != teamId, ct);
        if (inAnotherTeam)
            return Problem(
                "Fighter is already in another team of this tournament.",
                statusCode: 409);

        var member = new TeamMember
        {
            TeamId = teamId,
            FighterId = req.FighterId,
            Position = req.Position,
            AddedAt = DateTime.UtcNow,
        };

        db.TeamMembers.Add(member);
        await db.SaveChangesAsync(ct);

        var response = new TeamMemberResponse(
            req.FighterId, fighter.FirstName, fighter.LastName, fighter.Club,
            req.Position, member.AddedAt);
        return Created($"api/v1/teams/{teamId}/members/{req.FighterId}", response);
    }

    [HttpDelete("teams/{teamId:guid}/members/{fighterId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid teamId, Guid fighterId, CancellationToken ct)
    {
        var member = await db.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.FighterId == fighterId, ct);
        if (member is null) return NotFound();

        // Fighter cannot be removed if bound to a non-cancelled bout.
        var hasActiveBout = await db.Matches
            .AnyAsync(m => m.EncounterId != null
                        && (m.Fighter1Id == fighterId || m.Fighter2Id == fighterId)
                        && m.Status != MatchStatus.Cancelled, ct);
        if (hasActiveBout)
            return Problem(
                "Fighter is referenced by an active bout; cancel it first.",
                statusCode: 409);

        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
