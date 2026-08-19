namespace Zettel.Api.Dto.Fighters;

public record FighterResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string? Club,
    DateTime CreatedAt);
