using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zettel.Api.Common;
using Zettel.Api.Dto.Matches;
using Zettel.Api.Mapping;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Controllers;

[ApiController]
[Route("api/v1")]
public class ExchangesController(TournamentDbContext db) : ControllerBase
{
    [HttpPost("matches/{matchId:guid}/exchanges")]
    public async Task<IActionResult> Add(Guid matchId, AddExchangeRequest req, CancellationToken ct)
    {
        // Encounter is included for the same reason the placement is fetched below: both feed
        // the effective settings of the response (ТЗ §5.3). Without it a bout would report the
        // tournament default instead of its series duration.
        var match = await db.Matches
            .Include(m => m.Exchanges)
            .Include(m => m.Tournament)
            .Include(m => m.Encounter).ThenInclude(e => e!.Piste)
            .Include(m => m.Piste)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);

        if (match is null) return NotFound();

        if (match.Status != MatchStatus.InProgress)
            return Problem("Can only add exchanges to an InProgress match.", statusCode: 409);

        if (req.Points1 < 0 || req.Points2 < 0)
            return Problem("Points must be >= 0.", statusCode: 400);

        if (req.RoundNumber < 1)
            return Problem("RoundNumber must be >= 1.", statusCode: 400);

        var nextSequence = match.Exchanges.Count == 0 ? 1 : match.Exchanges.Max(e => e.Sequence) + 1;

        var exchange = new Exchange
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Sequence = nextSequence,
            RoundNumber = req.RoundNumber,
            Points1 = req.Points1,
            Points2 = req.Points2,
            IsDoubleHit = req.IsDoubleHit,
            Note = req.Note,
            CreatedAt = DateTime.UtcNow
        };

        match.Score1 += req.Points1;
        match.Score2 += req.Points2;

        db.Exchanges.Add(exchange);
        await db.SaveChangesAsync(ct);

        return Ok(match.ToResponse(match.Tournament, await db.PlacementOfAsync(matchId, ct)));
    }

    [HttpPut("exchanges/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, AddExchangeRequest req, CancellationToken ct)
    {
        var exchange = await db.Exchanges
            .Include(e => e.Match).ThenInclude(m => m.Exchanges)
            .Include(e => e.Match.Tournament)
            .Include(e => e.Match.Encounter).ThenInclude(x => x!.Piste)
            .Include(e => e.Match.Piste)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (exchange is null) return NotFound();

        var match = exchange.Match;

        if (match.Status != MatchStatus.InProgress)
            return Problem("Can only edit exchanges in an InProgress match.", statusCode: 409);

        if (req.Points1 < 0 || req.Points2 < 0)
            return Problem("Points must be >= 0.", statusCode: 400);

        if (req.RoundNumber < 1)
            return Problem("RoundNumber must be >= 1.", statusCode: 400);

        match.Score1 = match.Score1 - exchange.Points1 + req.Points1;
        match.Score2 = match.Score2 - exchange.Points2 + req.Points2;

        exchange.RoundNumber = req.RoundNumber;
        exchange.Points1 = req.Points1;
        exchange.Points2 = req.Points2;
        exchange.IsDoubleHit = req.IsDoubleHit;
        exchange.Note = req.Note;

        await db.SaveChangesAsync(ct);
        return Ok(match.ToResponse(match.Tournament, await db.PlacementOfAsync(match.Id, ct)));
    }

    [HttpDelete("exchanges/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var exchange = await db.Exchanges
            .Include(e => e.Match).ThenInclude(m => m.Exchanges)
            .Include(e => e.Match.Tournament)
            .Include(e => e.Match.Encounter).ThenInclude(x => x!.Piste)
            .Include(e => e.Match.Piste)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (exchange is null) return NotFound();

        var match = exchange.Match;

        if (match.Status != MatchStatus.InProgress)
            return Problem("Can only delete exchanges from an InProgress match.", statusCode: 409);

        match.Score1 -= exchange.Points1;
        match.Score2 -= exchange.Points2;

        db.Exchanges.Remove(exchange);
        await db.SaveChangesAsync(ct);

        return Ok(match.ToResponse(match.Tournament, await db.PlacementOfAsync(match.Id, ct)));
    }
}
