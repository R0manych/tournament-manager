namespace Zettel.Api.Dto.Encounters;

public record CreateTieBreakRequest(Guid Participant1Id, Guid Participant2Id);
