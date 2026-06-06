namespace TournamentManager.Api.Dto.Teams;

public record TeamResponse(
    Guid Id,
    Guid TournamentId,
    string Name,
    string? Club,
    string? City,
    DateTime CreatedAt,
    List<TeamMemberResponse> Members);

public record TeamMemberResponse(
    Guid FighterId,
    string FirstName,
    string LastName,
    string? Club,
    int Position,
    DateTime AddedAt);
