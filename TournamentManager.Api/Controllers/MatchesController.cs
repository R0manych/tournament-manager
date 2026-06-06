using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Matches;
using TournamentManager.Api.Mapping;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Enums;
using TournamentManager.Domain.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class MatchesController(TournamentDbContext db) : ControllerBase
{
    [HttpGet("tournaments/{tournamentId:guid}/matches")]
    public async Task<IActionResult> GetByTournament(Guid tournamentId, CancellationToken ct)
    {
        var tournamentExists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!tournamentExists) return NotFound();

        var matches = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .AsNoTracking()
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return Ok(matches.Select(m => m.ToResponse(m.Tournament)).ToList());
    }

    [HttpGet("matches/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        return match is null ? NotFound() : Ok(match.ToResponse(match.Tournament));
    }

    [HttpPost("tournaments/{tournamentId:guid}/matches")]
    public async Task<IActionResult> Create(Guid tournamentId, CreateMatchRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();

        if (req.Fighter2Id.HasValue && req.Fighter1Id == req.Fighter2Id.Value)
            return Problem("Fighter1 and Fighter2 must be different.", statusCode: 400);

        var f1InTournament = await db.TournamentParticipants
            .AnyAsync(p => p.TournamentId == tournamentId && p.FighterId == req.Fighter1Id, ct);
        if (!f1InTournament)
            return Problem($"Fighter {req.Fighter1Id} is not registered in this tournament.", statusCode: 400);

        if (req.Fighter2Id.HasValue)
        {
            var f2InTournament = await db.TournamentParticipants
                .AnyAsync(p => p.TournamentId == tournamentId && p.FighterId == req.Fighter2Id.Value, ct);
            if (!f2InTournament)
                return Problem($"Fighter {req.Fighter2Id} is not registered in this tournament.", statusCode: 400);
        }

        var now = DateTime.UtcNow;
        var isBye = !req.Fighter2Id.HasValue;

        var match = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Fighter1Id = req.Fighter1Id,
            Fighter2Id = req.Fighter2Id,
            ScheduledAt = req.ScheduledAt,
            Status = isBye ? MatchStatus.WalkoverWin : MatchStatus.Scheduled,
            WinnerId = isBye ? req.Fighter1Id : null,
            StartedAt = isBye ? now : null,
            EndedAt = isBye ? now : null,
            RoundDurationSeconds = req.RoundDurationSeconds,
            MaxDoubles = req.MaxDoubles,
            MaxWarnings = req.MaxWarnings,
            CreatedAt = now
        };

        db.Matches.Add(match);
        await db.SaveChangesAsync(ct);

        match.Tournament = tournament;
        return CreatedAtAction(nameof(GetById), new { id = match.Id }, match.ToResponse(tournament));
    }

    [HttpPatch("matches/{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, PatchMatchRequest req, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([id], ct);
        if (match is null) return NotFound();

        if (match.Status != MatchStatus.Scheduled)
            return Problem("Match settings can only be changed while status is Scheduled.", statusCode: 409);

        match.ScheduledAt = req.ScheduledAt;
        match.RoundDurationSeconds = req.RoundDurationSeconds;
        match.MaxDoubles = req.MaxDoubles;
        match.MaxWarnings = req.MaxWarnings;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("matches/{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, UpdateStatusRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<MatchStatus>(req.Status, out var newStatus))
            return Problem($"Invalid status value: '{req.Status}'.", statusCode: 400);

        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null) return NotFound();

        var (valid, error) = IsValidTransition(match.Status, newStatus);
        if (!valid) return Problem(error, statusCode: 409);

        var now = DateTime.UtcNow;

        switch (newStatus)
        {
            case MatchStatus.InProgress when match.Status == MatchStatus.Scheduled:
                match.StartedAt = now;
                match.CurrentRoundNumber = 1;
                match.CurrentRoundStartedAt = now;
                break;
            case MatchStatus.InProgress when match.Status == MatchStatus.Completed:
                match.EndedAt = null;
                match.WinnerId = null;
                break;
            case MatchStatus.Completed:
                match.EndedAt = now;
                match.WinnerId = CalcWinner(match);
                break;
            case MatchStatus.Cancelled:
                match.EndedAt = now;
                match.WinnerId = null;
                break;
        }

        match.Status = newStatus;
        await db.SaveChangesAsync(ct);

        return Ok(match.ToResponse(match.Tournament));
    }

    [HttpPatch("matches/{id:guid}/warnings")]
    public async Task<IActionResult> UpdateWarnings(Guid id, UpdateWarningsRequest req, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null) return NotFound();

        if (match.Status != MatchStatus.InProgress)
            return Problem("Warnings can only be changed while match is InProgress.", statusCode: 409);

        if (req.Fighter1Delta.HasValue)
            match.Warnings1 = Math.Max(0, match.Warnings1 + req.Fighter1Delta.Value);

        if (req.Fighter2Delta.HasValue)
            match.Warnings2 = Math.Max(0, match.Warnings2 + req.Fighter2Delta.Value);

        await db.SaveChangesAsync(ct);
        return Ok(match.ToResponse(match.Tournament));
    }

    [HttpPost("matches/{id:guid}/advance-round")]
    public async Task<IActionResult> AdvanceRound(Guid id, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (match is null) return NotFound();

        if (match.Status != MatchStatus.InProgress)
            return Problem("Can only advance round when match is InProgress.", statusCode: 409);

        match.CurrentRoundNumber++;
        match.CurrentRoundStartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(match.ToResponse(match.Tournament));
    }

    [HttpDelete("matches/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([id], ct);
        if (match is null) return NotFound();

        db.Matches.Remove(match);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("tournaments/{tournamentId:guid}/matches/generate-round-robin")]
    public async Task<IActionResult> GenerateRoundRobin(
        Guid tournamentId,
        GenerateRoundRobinRequest req,
        CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament is null) return NotFound();

        if (tournament.Format is null)
            return Problem("Tournament has no format uploaded.", statusCode: 400);

        var phase = tournament.Format.Phases.OfType<RoundRobinPhase>()
            .FirstOrDefault(p => p.Id == req.PhaseId);

        if (phase is null)
            return Problem($"Round-robin phase '{req.PhaseId}' not found in format.", statusCode: 400);

        // Validate all fighters belong to the tournament
        var registeredIds = tournament.Participants.Select(p => p.FighterId).ToHashSet();
        var allIds = req.Groups.SelectMany(g => g).ToList();
        var unknown = allIds.Where(id => !registeredIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            return Problem(
                $"Fighters not registered in this tournament: {string.Join(", ", unknown)}.",
                statusCode: 400);

        // Dedup within request itself
        if (allIds.Distinct().Count() != allIds.Count)
            return Problem("Fighter appears in more than one group or twice in the same group.", statusCode: 400);

        // Existing pairs for idempotency (normalised: smaller GUID first).
        // Walkovers (Fighter2Id == null) are tracked separately by Fighter1Id.
        var existingPairs = tournament.Matches
            .Where(m => m.Fighter2Id.HasValue)
            .Select(m => NormPair(m.Fighter1Id, m.Fighter2Id!.Value))
            .ToHashSet();
        var existingWalkovers = tournament.Matches
            .Where(m => !m.Fighter2Id.HasValue)
            .Select(m => m.Fighter1Id)
            .ToHashSet();

        var created = new List<Match>();
        int skipped = 0;
        var now = DateTime.UtcNow;
        var def = tournament.Format.Defaults;

        foreach (var group in req.Groups)
        {
            // Singleton group → walkover for the sole fighter.
            if (group.Count == 1)
            {
                if (!existingWalkovers.Add(group[0]))
                {
                    skipped++;
                    continue;
                }

                created.Add(new Match
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Fighter1Id = group[0],
                    Fighter2Id = null,
                    Status = MatchStatus.WalkoverWin,
                    WinnerId = group[0],
                    StartedAt = now,
                    EndedAt = now,
                    RoundDurationSeconds = def?.RoundDurationSeconds,
                    MaxDoubles = def?.MaxDoubles,
                    MaxWarnings = def?.MaxWarnings,
                    CreatedAt = now,
                });
                continue;
            }

            for (int i = 0; i < group.Count; i++)
            {
                for (int j = i + 1; j < group.Count; j++)
                {
                    var key = NormPair(group[i], group[j]);
                    if (!existingPairs.Add(key))
                    {
                        skipped++;
                        continue;
                    }

                    created.Add(new Match
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        Fighter1Id = group[i],
                        Fighter2Id = group[j],
                        Status = MatchStatus.Scheduled,
                        RoundDurationSeconds = def?.RoundDurationSeconds,
                        MaxDoubles = def?.MaxDoubles,
                        MaxWarnings = def?.MaxWarnings,
                        CreatedAt = now,
                    });
                }
            }
        }

        db.Matches.AddRange(created);
        await db.SaveChangesAsync(ct);

        var responses = created.Select(m => m.ToResponse(tournament)).ToList();
        return Ok(new GenerateRoundRobinResponse(created.Count, skipped, responses));
    }

    private static (Guid, Guid) NormPair(Guid a, Guid b) =>
        a < b ? (a, b) : (b, a);

    private static (bool valid, string error) IsValidTransition(MatchStatus from, MatchStatus to) =>
        (from, to) switch
        {
            (MatchStatus.Scheduled, MatchStatus.InProgress) => (true, ""),
            (MatchStatus.Scheduled, MatchStatus.Cancelled) => (true, ""),
            (MatchStatus.InProgress, MatchStatus.Completed) => (true, ""),
            (MatchStatus.InProgress, MatchStatus.Cancelled) => (true, ""),
            (MatchStatus.Completed, MatchStatus.InProgress) => (true, ""),
            _ => (false, $"Cannot transition from {from} to {to}.")
        };

    private static Guid? CalcWinner(Match m)
    {
        if (m.Score1 > m.Score2) return m.Fighter1Id;
        if (m.Score2 > m.Score1) return m.Fighter2Id;
        return null;
    }
}
