namespace TournamentManager.Api.Dto.Matches;

public record ExchangeResponse(
    Guid Id,
    int Sequence,
    int RoundNumber,
    int Points1,
    int Points2,
    bool IsDoubleHit,
    string? Note,
    DateTime CreatedAt);
