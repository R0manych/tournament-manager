using System.ComponentModel.DataAnnotations;

namespace TournamentManager.Api.Dto.Teams;

public record CreateTeamRequest(
    [Required][MaxLength(200)] string Name,
    [MaxLength(200)] string? Club,
    [MaxLength(100)] string? City);
