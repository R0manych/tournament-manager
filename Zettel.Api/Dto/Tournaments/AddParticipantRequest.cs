namespace Zettel.Api.Dto.Tournaments;

public record AddParticipantRequest(Guid ParticipantId, int? Seed);
