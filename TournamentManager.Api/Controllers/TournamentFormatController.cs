using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Infrastructure.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Api.Controllers;

[ApiController]
[Route("api/v1/tournaments/{tournamentId:guid}/format")]
public class TournamentFormatController(TournamentDbContext db, ITournamentFormatParser parser) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> UploadFormat(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();

        if (tournament.Matches.Count > 0)
            return Conflict(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-frozen",
                Title = "Tournament format is frozen",
                Status = 409,
                Detail = $"Tournament has {tournament.Matches.Count} matches; format cannot be changed.",
            });

        string yaml;
        if (Request.ContentType?.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
        {
            var file = Request.Form.Files.GetFile("file");
            if (file == null)
                return BadRequest(new ProblemDetails { Title = "Missing file", Detail = "Provide a file field in multipart/form-data." });
            using var reader = new StreamReader(file.OpenReadStream());
            yaml = await reader.ReadToEndAsync(ct);
        }
        else
        {
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            yaml = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(yaml))
            return BadRequest(new ProblemDetails { Title = "Empty body", Detail = "YAML body must not be empty." });

        var result = parser.Parse(yaml);
        if (!result.IsValid)
        {
            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-invalid",
                Title = "Tournament format is invalid",
                Status = 400,
            };
            problem.Extensions["errors"] = result.Errors;
            return new ObjectResult(problem) { StatusCode = 400 };
        }

        tournament.Format = result.Format;
        tournament.FormatYaml = yaml;
        await db.SaveChangesAsync(ct);

        return Ok(result.Format);
    }

    [HttpGet]
    public async Task<IActionResult> GetFormat(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();
        if (tournament.Format == null) return NotFound(new ProblemDetails { Detail = "No format uploaded for this tournament." });

        return Ok(tournament.Format);
    }

    [HttpGet("raw")]
    public async Task<IActionResult> GetFormatRaw(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();
        if (string.IsNullOrEmpty(tournament.FormatYaml)) return NotFound(new ProblemDetails { Detail = "No format uploaded for this tournament." });

        return Content(tournament.FormatYaml, "text/yaml");
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteFormat(Guid tournamentId, CancellationToken ct)
    {
        var tournament = await db.Tournaments
            .Include(t => t.Matches)
            .FirstOrDefaultAsync(t => t.Id == tournamentId, ct);

        if (tournament == null) return NotFound();

        if (tournament.Matches.Count > 0)
            return Conflict(new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7807#format-frozen",
                Title = "Tournament format is frozen",
                Status = 409,
                Detail = $"Tournament has {tournament.Matches.Count} matches; format cannot be deleted.",
            });

        tournament.Format = null;
        tournament.FormatYaml = null;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
