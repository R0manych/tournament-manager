using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Common;
using TournamentManager.Api.Dto.Matches;
using TournamentManager.Api.Dto.Placements;
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
            .Include(m => m.Encounter)
            .AsNoTracking()
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Placements ride along in MatchResponse so the bracket can be drawn from one
        // request; GET /placements exists for callers that only need the layout.
        var placements = await db.MatchPlacements
            .AsNoTracking()
            .Where(x => x.TournamentId == tournamentId)
            .ToDictionaryAsync(x => x.MatchId, ct);

        return Ok(matches
            .Select(m => m.ToResponse(m.Tournament, placements.GetValueOrDefault(m.Id)))
            .ToList());
    }

    [HttpGet("matches/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .Include(m => m.Encounter)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (match is null) return NotFound();

        var placement = await db.MatchPlacements
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MatchId == id, ct);

        return Ok(match.ToResponse(match.Tournament, placement));
    }

    [HttpPost("tournaments/{tournamentId:guid}/matches")]
    public async Task<IActionResult> Create(Guid tournamentId, CreateMatchRequest req, CancellationToken ct)
    {
        var tournament = await db.Tournaments.FindAsync([tournamentId], ct);
        if (tournament is null) return NotFound();

        if (req.Fighter2Id.HasValue && req.Fighter1Id == req.Fighter2Id.Value)
            return Problem("Participant1 and Participant2 must be different.", statusCode: 400);

        var p1InTournament = await db.TournamentParticipants
            .AnyAsync(p => p.TournamentId == tournamentId && p.ParticipantId == req.Fighter1Id, ct);
        if (!p1InTournament)
            return Problem($"Participant {req.Fighter1Id} is not registered in this tournament.", statusCode: 400);

        if (req.Fighter2Id.HasValue)
        {
            var p2InTournament = await db.TournamentParticipants
                .AnyAsync(p => p.TournamentId == tournamentId && p.ParticipantId == req.Fighter2Id.Value, ct);
            if (!p2InTournament)
                return Problem($"Participant {req.Fighter2Id} is not registered in this tournament.", statusCode: 400);
        }

        // Bracket cell, if the caller named one. Validated before anything is created, so a
        // bad placement never leaves a match behind (docs/08, §8).
        var requested = req.Placement;
        if (requested is not null)
        {
            var placementError = await PlacementGuard.ValidateAsync(db, tournament, requested, ct);
            if (placementError is not null) return Problem(placementError, statusCode: 400);

            var superseded = await PlacementGuard.EnsureNotSupersededAsync(db, tournament, requested.PhaseId, ct);
            if (superseded is not null)
                return Problem(superseded, title: "Phase is closed", statusCode: 409);

            var occupied = await db.MatchPlacements.AnyAsync(
                x => x.TournamentId == tournamentId
                     && x.PhaseId == requested.PhaseId
                     && x.RoundId == requested.RoundId
                     && x.SlotIndex == requested.SlotIndex, ct);

            // This 409 is what makes playoff generation idempotent: a repeated run collides
            // with the occupied cell instead of silently creating a duplicate fight.
            if (occupied)
                return Problem(
                    $"Bracket cell {requested.PhaseId}/{requested.RoundId}#{requested.SlotIndex} " +
                    "is already taken by another match.",
                    title: "Bracket cell is occupied",
                    statusCode: 409);
        }

        var now = DateTime.UtcNow;
        var isBye = !req.Fighter2Id.HasValue;
        var isTeam = tournament.ParticipantKind == ParticipantKind.Team;

        var match = new Match
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Fighter1Id = isTeam ? null : req.Fighter1Id,
            Fighter2Id = isTeam ? null : req.Fighter2Id,
            Team1Id = isTeam ? req.Fighter1Id : null,
            Team2Id = isTeam ? req.Fighter2Id : null,
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

        MatchPlacement? placement = null;
        if (requested is not null)
        {
            placement = new MatchPlacement
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                PhaseId = requested.PhaseId,
                RoundId = requested.RoundId,
                SlotIndex = requested.SlotIndex,
                MatchId = match.Id,
                CreatedAt = now,
            };
            db.MatchPlacements.Add(placement);
        }

        // First generated fight locks the setup stage (groups become read-only).
        if (tournament.Status == TournamentStatus.Draft)
            tournament.Status = TournamentStatus.Scheduled;

        await db.SaveChangesAsync(ct);

        match.Tournament = tournament;
        return CreatedAtAction(nameof(GetById), new { id = match.Id }, match.ToResponse(tournament, placement));
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
            .Include(m => m.Encounter)
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
                // First started fight moves the tournament to Active.
                if (match.Tournament.Status == TournamentStatus.Scheduled)
                    match.Tournament.Status = TournamentStatus.Active;
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

        // Cancelling frees the bracket cell, so the organiser can recreate the fight there
        // without an extra call (docs/08, ОВ-3). The cancelled match keeps existing, it just
        // stops belonging to the bracket.
        if (newStatus == MatchStatus.Cancelled)
        {
            var freed = await db.MatchPlacements.FirstOrDefaultAsync(x => x.MatchId == id, ct);
            if (freed is not null) db.MatchPlacements.Remove(freed);
        }

        match.Status = newStatus;
        await db.SaveChangesAsync(ct);

        var current = newStatus == MatchStatus.Cancelled
            ? null
            : await db.MatchPlacements.AsNoTracking().FirstOrDefaultAsync(x => x.MatchId == id, ct);
        return Ok(match.ToResponse(match.Tournament, current));
    }

    [HttpPatch("matches/{id:guid}/warnings")]
    public async Task<IActionResult> UpdateWarnings(Guid id, UpdateWarningsRequest req, CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .Include(m => m.Encounter)
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
            .Include(m => m.Encounter)
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

        if (match.EncounterId is not null)
            return Problem(
                "Bouts cannot be deleted individually; delete the parent encounter.",
                statusCode: 409);

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

        if (tournament.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
            return Problem($"Cannot generate matches for a {tournament.Status} tournament.", statusCode: 409);

        if (tournament.Format is null)
            return Problem("Tournament has no format uploaded.", statusCode: 400);

        var phase = tournament.Format.Phases.OfType<RoundRobinPhase>()
            .FirstOrDefault(p => p.Id == req.PhaseId);

        if (phase is null)
            return Problem($"Round-robin phase '{req.PhaseId}' not found in format.", statusCode: 400);

        var superseded = await PlacementGuard.EnsureNotSupersededAsync(db, tournament, req.PhaseId, ct);
        if (superseded is not null)
            return Problem(superseded, title: "Phase is closed", statusCode: 409);

        // Groups omitted → use the saved composition of this phase. Labels come along now:
        // the bracket cell of a group-stage match is (phase, group label, pair index),
        // see docs/08 §7.
        List<(string Label, List<Guid> Ids)> labelled;
        if (req.Groups is { Count: > 0 })
        {
            // Groups passed in the body carry no labels — fall back to the A, B, C…
            // convention the format and the UI use for group codes.
            labelled = req.Groups.Select((ids, i) => (GroupLabel(i), ids)).ToList();
        }
        else
        {
            var saved = await db.TournamentGroups
                .AsNoTracking()
                .Where(g => g.TournamentId == tournamentId && g.PhaseId == req.PhaseId)
                .OrderBy(g => g.OrderIndex)
                .Select(g => new { g.Label, g.ParticipantIds })
                .ToListAsync(ct);

            if (saved.Count == 0)
                return Problem(
                    $"No saved groups for phase '{req.PhaseId}'. Save groups first or pass them in the request.",
                    statusCode: 400);

            labelled = saved.Select(g => (g.Label, g.ParticipantIds)).ToList();
        }

        var groups = labelled.Select(g => g.Ids).ToList();

        // Validate all participants belong to the tournament
        var registeredIds = tournament.Participants.Select(p => p.ParticipantId).ToHashSet();
        var allIds = groups.SelectMany(g => g).ToList();
        var unknown = allIds.Where(id => !registeredIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            return Problem(
                $"Participants not registered in this tournament: {string.Join(", ", unknown)}.",
                statusCode: 400);

        // Dedup within request itself
        if (allIds.Distinct().Count() != allIds.Count)
            return Problem("Participant appears in more than one group or twice in the same group.", statusCode: 400);

        var isTeam = tournament.ParticipantKind == ParticipantKind.Team;

        // Idempotency runs on cells alone. It used to run on pairs — "these two have already
        // met somewhere in this tournament" — which is a broader rule than the domain wants:
        // the same two may legitimately meet again in a later phase, and in doubleElimination
        // they meet twice within one phase (grand final and reset). A cell is the honest unit.
        var occupiedCells = (await db.MatchPlacements
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId && x.PhaseId == req.PhaseId)
                .Select(x => new { x.RoundId, x.SlotIndex })
                .ToListAsync(ct))
            .Select(x => (x.RoundId, x.SlotIndex))
            .ToHashSet();

        var created = new List<Match>();
        var createdPlacements = new List<MatchPlacement>();
        int skipped = 0;
        var now = DateTime.UtcNow;
        var def = tournament.Format.Defaults;

        void Place(Match m, string label, int slotIndex) =>
            createdPlacements.Add(new MatchPlacement
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                PhaseId = req.PhaseId,
                RoundId = label,
                SlotIndex = slotIndex,
                MatchId = m.Id,
                CreatedAt = now,
            });

        foreach (var (label, group) in labelled)
        {
            // Singleton group → walkover for the sole participant.
            if (group.Count == 1)
            {
                if (!occupiedCells.Add((label, 0)))
                {
                    skipped++;
                    continue;
                }

                var bye = new Match
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Fighter1Id = isTeam ? null : group[0],
                    Team1Id = isTeam ? group[0] : null,
                    Status = MatchStatus.WalkoverWin,
                    WinnerId = group[0],
                    StartedAt = now,
                    EndedAt = now,
                    RoundDurationSeconds = def?.RoundDurationSeconds,
                    MaxDoubles = def?.MaxDoubles,
                    MaxWarnings = def?.MaxWarnings,
                    CreatedAt = now,
                };
                created.Add(bye);
                Place(bye, label, 0);
                continue;
            }

            // slotIndex counts every candidate pair, skipped ones included: the cell of a
            // pair must not move just because an earlier pair already existed.
            var slotIndex = 0;
            for (int i = 0; i < group.Count; i++)
            {
                for (int j = i + 1; j < group.Count; j++, slotIndex++)
                {
                    if (!occupiedCells.Add((label, slotIndex)))
                    {
                        skipped++;
                        continue;
                    }

                    var match = new Match
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        Fighter1Id = isTeam ? null : group[i],
                        Fighter2Id = isTeam ? null : group[j],
                        Team1Id = isTeam ? group[i] : null,
                        Team2Id = isTeam ? group[j] : null,
                        Status = MatchStatus.Scheduled,
                        RoundDurationSeconds = def?.RoundDurationSeconds,
                        MaxDoubles = def?.MaxDoubles,
                        MaxWarnings = def?.MaxWarnings,
                        CreatedAt = now,
                    };
                    created.Add(match);
                    Place(match, label, slotIndex);
                }
            }
        }

        db.Matches.AddRange(created);
        db.MatchPlacements.AddRange(createdPlacements);

        // Generation completes the setup stage: groups get locked.
        if (tournament.Status == TournamentStatus.Draft)
            tournament.Status = TournamentStatus.Scheduled;

        await db.SaveChangesAsync(ct);

        var placementByMatch = createdPlacements.ToDictionary(x => x.MatchId);
        var responses = created
            .Select(m => m.ToResponse(tournament, placementByMatch.GetValueOrDefault(m.Id)))
            .ToList();
        return Ok(new GenerateRoundRobinResponse(created.Count, skipped, responses));
    }

    // Group codes as the format writes them: groups.A, groups.B, … Beyond 26 groups the
    // letters run out; those get a positional label, which is still unique within the phase.
    private static string GroupLabel(int index) =>
        index < 26 ? ((char)('A' + index)).ToString() : $"G{index + 1}";

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
        if (m.Score1 > m.Score2) return m.Team1Id ?? m.Fighter1Id;
        if (m.Score2 > m.Score1) return m.Team2Id ?? m.Fighter2Id;
        return null;
    }
}
