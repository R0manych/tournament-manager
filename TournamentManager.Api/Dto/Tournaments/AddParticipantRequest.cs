namespace TournamentManager.Api.Dto.Tournaments;

public record AddParticipantRequest(Guid ParticipantId, int? Seed);
