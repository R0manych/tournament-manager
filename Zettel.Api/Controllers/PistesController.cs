using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zettel.Api.Common;
using Zettel.Api.Dto.Pistes;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class PistesController(TournamentDbContext db) : ControllerBase
{
    [HttpGet("tournaments/{tournamentId:guid}/pistes")]
    public async Task<IActionResult> GetByTournament(Guid tournamentId, CancellationToken ct)
    {
        var tournamentExists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!tournamentExists) return NotFound();

        var pistes = await db.Pistes.AsNoTracking()
            .Where(p => p.TournamentId == tournamentId)
            .OrderBy(p => p.OrderIndex).ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

        // Занятость собирается одним проходом по идущим встречам турнира, а не запросом на
        // каждое ристалище: площадок единицы, идущих встреч тоже.
        var runningMatches = await db.Matches.AsNoTracking()
            .Where(m => m.TournamentId == tournamentId && m.Status == MatchStatus.InProgress)
            .Select(m => new
            {
                m.Id,
                m.EncounterId,
                EffectivePisteId = m.PisteId ?? (m.Encounter != null ? m.Encounter.PisteId : null),
            })
            .Where(x => x.EffectivePisteId != null)
            .ToListAsync(ct);

        var runningEncounters = await db.Encounters.AsNoTracking()
            .Where(e => e.TournamentId == tournamentId
                        && e.Status == MatchStatus.InProgress
                        && e.PisteId != null)
            .Select(e => new { e.Id, e.PisteId })
            .ToListAsync(ct);

        return Ok(pistes.Select(p =>
        {
            var current = runningMatches.FirstOrDefault(x => x.EffectivePisteId == p.Id);
            var encounterId = current?.EncounterId
                ?? runningEncounters.FirstOrDefault(e => e.PisteId == p.Id)?.Id;
            return new PisteResponse(
                p.Id, p.TournamentId, p.Name, p.OrderIndex, p.CreatedAt,
                current?.Id, encounterId);
        }).ToList());
    }

    [HttpGet("pistes/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var piste = await db.Pistes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return piste is null ? NotFound() : Ok(await ToResponseAsync(piste, ct));
    }

    [HttpPost("tournaments/{tournamentId:guid}/pistes")]
    public async Task<IActionResult> Create(
        Guid tournamentId, CreatePisteRequest req, CancellationToken ct)
    {
        var tournamentExists = await db.Tournaments.AnyAsync(t => t.Id == tournamentId, ct);
        if (!tournamentExists) return NotFound();

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Problem("Piste name is required.", statusCode: 400);

        if (await db.Pistes.AnyAsync(p => p.TournamentId == tournamentId && p.Name == name, ct))
            return Problem($"Piste '{name}' already exists in this tournament.",
                title: "Duplicate piste name", statusCode: 409);

        // Порядок по умолчанию — следующий свободный: организатор заводит площадки подряд и
        // номеровать их руками не должен.
        var orderIndex = req.OrderIndex ?? await NextOrderIndexAsync(tournamentId, ct);

        var piste = new Piste
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Name = name,
            OrderIndex = orderIndex,
            CreatedAt = DateTime.UtcNow,
        };

        db.Pistes.Add(piste);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = piste.Id },
            new PisteResponse(piste.Id, piste.TournamentId, piste.Name, piste.OrderIndex,
                piste.CreatedAt, null, null));
    }

    // Турнир ристалища не меняется (инвариант 52), поэтому правятся только имя и порядок.
    [HttpPut("pistes/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdatePisteRequest req, CancellationToken ct)
    {
        var piste = await db.Pistes.FindAsync([id], ct);
        if (piste is null) return NotFound();

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return Problem("Piste name is required.", statusCode: 400);

        var duplicate = await db.Pistes.AnyAsync(
            p => p.TournamentId == piste.TournamentId && p.Name == name && p.Id != id, ct);
        if (duplicate)
            return Problem($"Piste '{name}' already exists in this tournament.",
                title: "Duplicate piste name", statusCode: 409);

        piste.Name = name;
        piste.OrderIndex = req.OrderIndex;

        await db.SaveChangesAsync(ct);
        return Ok(await ToResponseAsync(piste, ct));
    }

    [HttpDelete("pistes/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var piste = await db.Pistes.FindAsync([id], ct);
        if (piste is null) return NotFound();

        // Инвариант 57: незавершённые назначения удалить площадку не дают — иначе бои молча
        // остались бы без ристалища и пропали с табло. Назначения завершённых встреч
        // удалению не мешают: это исторический факт, и ссылка на них обнуляется каскадом.
        var pendingMatches = await db.Matches.AnyAsync(
            m => (m.PisteId == id || (m.Encounter != null && m.Encounter.PisteId == id))
                 && (m.Status == MatchStatus.Scheduled || m.Status == MatchStatus.InProgress), ct);

        var pendingEncounters = await db.Encounters.AnyAsync(
            e => e.PisteId == id
                 && (e.Status == MatchStatus.Scheduled || e.Status == MatchStatus.InProgress), ct);

        if (pendingMatches || pendingEncounters)
            return Problem(
                "Piste still has scheduled or running matches assigned. " +
                "Move them to another piste or clear the assignment first.",
                title: "Piste is in use", statusCode: 409);

        db.Pistes.Remove(piste);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<int> NextOrderIndexAsync(Guid tournamentId, CancellationToken ct)
    {
        var max = await db.Pistes
            .Where(p => p.TournamentId == tournamentId)
            .Select(p => (int?)p.OrderIndex)
            .MaxAsync(ct);
        return (max ?? -1) + 1;
    }

    private async Task<PisteResponse> ToResponseAsync(Piste piste, CancellationToken ct)
    {
        var current = await db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.InProgress)
            .Where(m => m.PisteId == piste.Id
                        || (m.Encounter != null && m.Encounter.PisteId == piste.Id))
            .Select(m => new { m.Id, m.EncounterId })
            .FirstOrDefaultAsync(ct);

        var encounterId = current?.EncounterId
            ?? await db.Encounters.AsNoTracking()
                .Where(e => e.PisteId == piste.Id && e.Status == MatchStatus.InProgress)
                .Select(e => (Guid?)e.Id)
                .FirstOrDefaultAsync(ct);

        return new PisteResponse(
            piste.Id, piste.TournamentId, piste.Name, piste.OrderIndex, piste.CreatedAt,
            current?.Id, encounterId);
    }
}
