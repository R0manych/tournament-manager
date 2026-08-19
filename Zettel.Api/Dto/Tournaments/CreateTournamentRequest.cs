using System.ComponentModel.DataAnnotations;

namespace Zettel.Api.Dto.Tournaments;

public record CreateTournamentRequest(
    [Required][MaxLength(200)] string Name,
    [MaxLength(4000)] string? Description,
    [MaxLength(100)] string? Nomination,
    [MaxLength(200)] string? Location,
    DateOnly StartDate,
    DateOnly EndDate,
    string? ParticipantKind,
    [Range(1, int.MaxValue)] int? DefaultRoundDurationSeconds,
    [Range(0, int.MaxValue)] int? DefaultMaxDoubles,
    [Range(0, int.MaxValue)] int? DefaultMaxWarnings,
    [Range(1, int.MaxValue)] int? DefaultTeamTargetScore,
    [Range(1, int.MaxValue)] int? DefaultTeamBoutDurationSeconds);
