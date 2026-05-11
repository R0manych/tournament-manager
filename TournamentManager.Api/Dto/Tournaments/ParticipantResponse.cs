namespace TournamentManager.Api.Dto.Tournaments;

public record ParticipantResponse(
    Guid FighterId,
    string FirstName,
    string LastName,
    string? Club,
    int? Seed,
    DateTime RegisteredAt);
