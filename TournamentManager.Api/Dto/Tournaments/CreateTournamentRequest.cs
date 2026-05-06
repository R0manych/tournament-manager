using System.ComponentModel.DataAnnotations;

namespace TournamentManager.Api.Dto.Tournaments;

public record CreateTournamentRequest(
    [Required][MaxLength(200)] string Name,
    [MaxLength(4000)] string? Description,
    [MaxLength(100)] string? Nomination,
    [MaxLength(200)] string? Location,
    DateOnly StartDate,
    DateOnly EndDate,
    [Range(1, int.MaxValue)] int? DefaultRoundDurationSeconds,
    [Range(1, int.MaxValue)] int? DefaultRoundsPerMatch,
    [Range(0, int.MaxValue)] int? DefaultMaxDoubles,
    [Range(0, int.MaxValue)] int? DefaultMaxWarnings);
