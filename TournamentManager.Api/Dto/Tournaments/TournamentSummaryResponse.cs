namespace TournamentManager.Api.Dto.Tournaments;

public record TournamentSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Nomination,
    string? Location,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    int? DefaultRoundDurationSeconds,
    int? DefaultRoundsPerMatch,
    int? DefaultMaxDoubles,
    int? DefaultMaxWarnings,
    DateTime CreatedAt,
    int ParticipantsCount,
    int MatchesCount);
