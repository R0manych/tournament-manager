namespace TournamentManager.Api.Dto.Tournaments;

public record TournamentResponse(
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
    List<ParticipantResponse> Participants,
    int MatchesCount);
