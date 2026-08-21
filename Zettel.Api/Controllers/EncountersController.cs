using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zettel.Api.Dto.Encounters;
using Zettel.Api.Dto.Matches;
using Zettel.Api.Mapping;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;
using Zettel.Infrastructure.Encounters;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class EncountersController(TournamentDbContext db) : ControllerBase
{
    [HttpGet("tournaments/{tournamentId:guid}/encounters")]
    public async Task<IActionResult> GetByTournament(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();

        var encounters = await db.Encounters
            .Include(e => e.Bouts).ThenInclude(b => b.Exchanges)
            .AsNoTracking()
            .Where(e => e.TournamentId == tournamentId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return Ok(encounters.Select(e => e.ToResponse(tournament)).ToList());
    }

    [HttpGet("encounters/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var encounter = await db.Encounters
            .Include(e => e.Bouts).ThenInclude(b => b.Exchanges)
            .Include(e => e.Tournament)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return encounter is null ? NotFound() : Ok(encounter.ToResponse(encounter.Tournament));
    }

    [HttpPost("tournaments/{tournamentId:guid}/encounters")]
    public async Task<IActionResult> Create(
        Guid tournamentId, CreateEncounterRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();
        if (tournament.ParticipantKind != ParticipantKind.Team)
            return Problem("Tournament is not a team tournament.", statusCode: 409);

        if (req.Participant1Id == req.Participant2Id)
            return Problem("Participant1 and Participant2 must be different.", statusCode: 400);

        var registeredIds = await db.TournamentParticipants
            .Where(p => p.TournamentId == tournamentId)
            .Select(p => p.ParticipantId)
            .ToListAsync(ct);

        if (!registeredIds.Contains(req.Participant1Id))
            return Problem(
                $"Team {req.Participant1Id} is not registered in this tournament.",
                statusCode: 400);
        if (!registeredIds.Contains(req.Participant2Id))
            return Problem(
                $"Team {req.Participant2Id} is not registered in this tournament.",
                statusCode: 400);

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Participant1Id = req.Participant1Id,
            Participant2Id = req.Participant2Id,
            ScheduledAt = req.ScheduledAt,
            Status = MatchStatus.Scheduled,
            TargetTotalScore = req.TargetTotalScore
                ?? tournament.DefaultTeamTargetScore
                ?? 45,
            BoutDurationSeconds = req.BoutDurationSeconds
                ?? tournament.DefaultTeamBoutDurationSeconds
                ?? 60,
            CreatedAt = DateTime.UtcNow,
        };

        db.Encounters.Add(encounter);

        // First generated encounter locks the setup stage (groups become read-only).
        if (tournament.Status == TournamentStatus.Draft)
            tournament.Status = TournamentStatus.Scheduled;

        await db.SaveChangesAsync(ct);

        return CreatedAtAction(
            nameof(GetById), new { id = encounter.Id },
            encounter.ToResponse(tournament));
    }

    [HttpPatch("encounters/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(
        Guid id, UpdateStatusRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<MatchStatus>(req.Status, out var newStatus))
            return Problem($"Invalid status value: '{req.Status}'.", statusCode: 400);

        var encounter = await db.Encounters
            .Include(e => e.Bouts).ThenInclude(b => b.Exchanges)
            .Include(e => e.Tournament)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (encounter is null) return NotFound();

        var (valid, error) = IsValidTransition(encounter.Status, newStatus);
        if (!valid) return Problem(error, statusCode: 409);

        var now = DateTime.UtcNow;

        switch (newStatus)
        {
            case MatchStatus.InProgress when encounter.Status == MatchStatus.Scheduled:
                encounter.StartedAt = now;
                // First started encounter moves the tournament to Active.
                if (encounter.Tournament.Status == TournamentStatus.Scheduled)
                    encounter.Tournament.Status = TournamentStatus.Active;
                break;

            case MatchStatus.InProgress when encounter.Status is MatchStatus.Completed or MatchStatus.DoubleLoss:
                encounter.EndedAt = null;
                encounter.WinnerParticipantId = null;
                if (encounter.StartedAt is null)
                {
                    encounter.StartedAt = now;
                    if (encounter.Tournament.Status == TournamentStatus.Scheduled)
                        encounter.Tournament.Status = TournamentStatus.Active;
                }
                break;

            case MatchStatus.DoubleLoss:
                // Both teams lose. TryComputeWinner is bypassed on purpose: on a tied score it
                // would demand a tie-break, and here there is nothing left to break (АР-16).
                // Unplayed bouts inside the series are left alone — the organiser did not ask
                // to void them, and the series is terminal anyway.
                encounter.EndedAt = now;
                encounter.WinnerParticipantId = null;
                break;

            case MatchStatus.Completed:
                var (canComplete, completeError, winner) = TryComputeWinner(encounter);
                if (!canComplete) return Problem(completeError, statusCode: 409);
                encounter.EndedAt = now;
                encounter.WinnerParticipantId = winner;
                break;

            case MatchStatus.Cancelled:
                encounter.EndedAt = now;
                encounter.WinnerParticipantId = null;
                break;
        }

        encounter.Status = newStatus;
        await db.SaveChangesAsync(ct);

        return Ok(encounter.ToResponse(encounter.Tournament));
    }

    [HttpPost("encounters/{id:guid}/generate-bouts")]
    public async Task<IActionResult> GenerateBouts(Guid id, CancellationToken ct)
    {
        var encounter = await db.Encounters
            .Include(e => e.Bouts).ThenInclude(b => b.Exchanges)
            .Include(e => e.Tournament)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (encounter is null) return NotFound();

        // Idempotent: if all 9 already exist, just return current encounter.
        var existingNumbers = encounter.Bouts
            .Where(b => b.BoutNumber is >= 1 and <= 9)
            .Select(b => b.BoutNumber!.Value)
            .ToHashSet();

        if (existingNumbers.Count == TeamPairingSchedule.Fie3v3.Count)
            return Ok(encounter.ToResponse(encounter.Tournament));

        var team1Members = await db.TeamMembers
            .Where(tm => tm.TeamId == encounter.Participant1Id)
            .OrderBy(tm => tm.Position)
            .ToListAsync(ct);
        var team2Members = await db.TeamMembers
            .Where(tm => tm.TeamId == encounter.Participant2Id)
            .OrderBy(tm => tm.Position)
            .ToListAsync(ct);

        if (team1Members.Count != TeamPairingSchedule.TeamSize
            || team2Members.Count != TeamPairingSchedule.TeamSize)
            return Problem(
                $"Both teams must have exactly {TeamPairingSchedule.TeamSize} members " +
                "with positions 1, 2, 3 before generating bouts.",
                statusCode: 409);

        if (team1Members.Select(m => m.Position).Distinct().Count() != TeamPairingSchedule.TeamSize
            || team2Members.Select(m => m.Position).Distinct().Count() != TeamPairingSchedule.TeamSize)
            return Problem("Team positions must be unique 1..3.", statusCode: 409);

        var t1ByPos = team1Members.ToDictionary(m => m.Position, m => m.FighterId);
        var t2ByPos = team2Members.ToDictionary(m => m.Position, m => m.FighterId);

        var now = DateTime.UtcNow;
        var created = new List<Match>();

        foreach (var spec in TeamPairingSchedule.Fie3v3)
        {
            if (existingNumbers.Contains(spec.BoutNumber)) continue;

            created.Add(new Match
            {
                Id = Guid.NewGuid(),
                TournamentId = encounter.TournamentId,
                Fighter1Id = t1ByPos[spec.APosition],
                Fighter2Id = t2ByPos[spec.BPosition],
                Status = MatchStatus.Scheduled,
                EncounterId = encounter.Id,
                BoutNumber = spec.BoutNumber,
                TargetCumulativeScore = spec.TargetCumulativeScore,
                RoundDurationSeconds = encounter.BoutDurationSeconds,
                CreatedAt = now,
            });
        }

        db.Matches.AddRange(created);
        await db.SaveChangesAsync(ct);

        // Reload to return up-to-date list of bouts.
        await db.Entry(encounter).Collection(e => e.Bouts).LoadAsync(ct);
        foreach (var bout in encounter.Bouts.Where(b => b.Exchanges.Count == 0))
            await db.Entry(bout).Collection(b => b.Exchanges).LoadAsync(ct);

        return Ok(encounter.ToResponse(encounter.Tournament));
    }

    [HttpPost("encounters/{id:guid}/tiebreak")]
    public async Task<IActionResult> CreateTieBreak(
        Guid id, CreateTieBreakRequest req, CancellationToken ct)
    {
        var encounter = await db.Encounters
            .Include(e => e.Bouts).ThenInclude(b => b.Exchanges)
            .Include(e => e.Tournament)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (encounter is null) return NotFound();

        if (req.Participant1Id == req.Participant2Id)
            return Problem("Tie-break fighters must be different.", statusCode: 400);

        var basicBouts = encounter.Bouts.Where(b => b.BoutNumber is >= 1 and <= 9).ToList();
        // A bout stopped by a double loss is finished and still counts towards the total, so
        // it must not block the tie-break: otherwise a series tied because of such a bout can
        // neither be completed (TryComputeWinner demands a tie-break) nor get one.
        if (basicBouts.Count != 9
            || basicBouts.Any(b => b.Status is not (MatchStatus.Completed or MatchStatus.DoubleLoss)))
            return Problem(
                "All 9 basic bouts must be completed before creating a tie-break.",
                statusCode: 409);

        var score1 = basicBouts.Sum(b => b.Score1);
        var score2 = basicBouts.Sum(b => b.Score2);
        if (score1 != score2)
            return Problem("Encounter is not tied; tie-break not required.", statusCode: 409);

        if (encounter.Bouts.Any(b => b.BoutNumber == 10))
            return Problem("Tie-break bout already exists.", statusCode: 409);

        var f1InTeam1 = await db.TeamMembers.AnyAsync(
            tm => tm.TeamId == encounter.Participant1Id && tm.FighterId == req.Participant1Id, ct);
        if (!f1InTeam1)
            return Problem(
                $"Fighter {req.Participant1Id} is not in team {encounter.Participant1Id}.",
                statusCode: 400);

        var f2InTeam2 = await db.TeamMembers.AnyAsync(
            tm => tm.TeamId == encounter.Participant2Id && tm.FighterId == req.Participant2Id, ct);
        if (!f2InTeam2)
            return Problem(
                $"Fighter {req.Participant2Id} is not in team {encounter.Participant2Id}.",
                statusCode: 400);

        var now = DateTime.UtcNow;
        var tieBreak = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = encounter.TournamentId,
            Fighter1Id = req.Participant1Id,
            Fighter2Id = req.Participant2Id,
            Status = MatchStatus.Scheduled,
            EncounterId = encounter.Id,
            BoutNumber = 10,
            TargetCumulativeScore = null,
            RoundDurationSeconds = 60,
            CreatedAt = now,
        };

        encounter.PriorityParticipantId = Random.Shared.Next(2) == 0
            ? encounter.Participant1Id
            : encounter.Participant2Id;

        db.Matches.Add(tieBreak);
        await db.SaveChangesAsync(ct);

        await db.Entry(encounter).Collection(e => e.Bouts).LoadAsync(ct);
        return Ok(encounter.ToResponse(encounter.Tournament));
    }

    [HttpDelete("encounters/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var encounter = await db.Encounters
            .Include(e => e.Bouts)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (encounter is null) return NotFound();

        if (encounter.Bouts.Any(b => b.Status is MatchStatus.Completed or MatchStatus.DoubleLoss))
            return Problem(
                "Encounter has completed bouts; cancel them first.",
                statusCode: 409);

        db.Encounters.Remove(encounter);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static (bool valid, string error) IsValidTransition(MatchStatus from, MatchStatus to) =>
        (from, to) switch
        {
            (MatchStatus.Scheduled, MatchStatus.InProgress) => (true, ""),
            (MatchStatus.Scheduled, MatchStatus.Cancelled) => (true, ""),
            (MatchStatus.InProgress, MatchStatus.Completed) => (true, ""),
            (MatchStatus.InProgress, MatchStatus.Cancelled) => (true, ""),
            (MatchStatus.Completed, MatchStatus.InProgress) => (true, ""),
            // Double loss for the whole series — same four transitions as a single fight.
            (MatchStatus.Scheduled, MatchStatus.DoubleLoss) => (true, ""),
            (MatchStatus.InProgress, MatchStatus.DoubleLoss) => (true, ""),
            (MatchStatus.Completed, MatchStatus.DoubleLoss) => (true, ""),
            (MatchStatus.DoubleLoss, MatchStatus.InProgress) => (true, ""),
            _ => (false, $"Cannot transition encounter from {from} to {to}.")
        };

    private static (bool canComplete, string error, Guid? winner) TryComputeWinner(Encounter e)
    {
        // DoubleLoss bouts keep contributing: the score they were stopped at is real and must
        // not vanish from the series total the moment the status is set (АР-16). The same
        // filter lives in EncounterMappingExtensions — both have to agree.
        var contributing = e.Bouts
            .Where(m => m.Status is MatchStatus.InProgress or MatchStatus.Completed or MatchStatus.DoubleLoss)
            .ToList();
        var score1 = contributing.Sum(m => m.Score1);
        var score2 = contributing.Sum(m => m.Score2);

        if (score1 == score2)
        {
            var tieBreak = e.Bouts.FirstOrDefault(b => b.BoutNumber == 10);
            if (tieBreak is null)
                return (false,
                    "Encounter is tied; create a tie-break bout via POST /encounters/{id}/tiebreak.",
                    null);
            if (tieBreak.Status != MatchStatus.Completed)
                return (false, "Tie-break bout is not completed yet.", null);

            // Tie-break bout decides — fall through to winner-from-bout logic.
            if (tieBreak.Score1 > tieBreak.Score2) return (true, "", e.Participant1Id);
            if (tieBreak.Score2 > tieBreak.Score1) return (true, "", e.Participant2Id);
            // 0:0 — priority decides.
            return (true, "", e.PriorityParticipantId);
        }

        return (true, "", score1 > score2 ? e.Participant1Id : e.Participant2Id);
    }
}
