namespace TournamentManager.Api.Dto.Tournaments;

public record AddParticipantRequest(Guid FighterId, int? Seed);
