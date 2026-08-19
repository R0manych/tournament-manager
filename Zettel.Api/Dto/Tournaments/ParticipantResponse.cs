namespace Zettel.Api.Dto.Tournaments;

public record ParticipantResponse(
    Guid ParticipantId,
    string Kind,
    FighterParticipantInfo? Fighter,
    TeamParticipantInfo? Team,
    int? Seed,
    DateTime RegisteredAt);

public record FighterParticipantInfo(string FirstName, string LastName, string? Club);

public record TeamParticipantInfo(string Name, string? Club, string? City);
