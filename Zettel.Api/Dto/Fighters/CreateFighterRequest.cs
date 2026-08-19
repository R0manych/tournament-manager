using System.ComponentModel.DataAnnotations;

namespace Zettel.Api.Dto.Fighters;

public record CreateFighterRequest(
    [Required][MaxLength(100)] string FirstName,
    [Required][MaxLength(100)] string LastName,
    [MaxLength(200)] string? Club,
    [MaxLength(100)] string? City,
    DateOnly? BirthDate);
