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
    string ParticipantKind,
    int? DefaultRoundDurationSeconds,
    int? DefaultMaxDoubles,
    int? DefaultMaxWarnings,
    int? DefaultTeamTargetScore,
    int? DefaultTeamBoutDurationSeconds,
    DateTime CreatedAt,
    List<ParticipantResponse> Participants,
    int MatchesCount);
