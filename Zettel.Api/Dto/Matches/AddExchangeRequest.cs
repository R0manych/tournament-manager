namespace Zettel.Api.Dto.Matches;

public record AddExchangeRequest(
    int RoundNumber,
    int Points1,
    int Points2,
    bool IsDoubleHit,
    string? Note);
