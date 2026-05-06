using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Api.Dto.Fighters;
using TournamentManager.Api.Mapping;
using TournamentManager.Domain.Entities;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1/fighters")]
public class FightersController(TournamentDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var fighters = await db.Fighters
            .AsNoTracking()
            .OrderBy(f => f.LastName).ThenBy(f => f.FirstName)
            .ToListAsync(ct);

        return Ok(fighters.Select(f => f.ToResponse()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var fighter = await db.Fighters.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        return fighter is null ? NotFound() : Ok(fighter.ToResponse());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateFighterRequest req, CancellationToken ct)
    {
        var fighter = new Fighter
        {
            Id = Guid.NewGuid(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Club = req.Club,
            CreatedAt = DateTime.UtcNow
        };

        db.Fighters.Add(fighter);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = fighter.Id }, fighter.ToResponse());
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CreateFighterRequest req, CancellationToken ct)
    {
        var fighter = await db.Fighters.FindAsync([id], ct);
        if (fighter is null) return NotFound();

        fighter.FirstName = req.FirstName;
        fighter.LastName = req.LastName;
        fighter.Club = req.Club;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var fighter = await db.Fighters.FindAsync([id], ct);
        if (fighter is null) return NotFound();

        try
        {
            db.Fighters.Remove(fighter);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateException)
        {
            return Problem(
                "Cannot delete fighter because they have existing tournament participations or matches.",
                statusCode: 409);
        }
    }
}
