namespace Zettel.Api.Dto.Placements;

// Cell of the bracket a match occupies. Sent with POST /tournaments/{id}/matches and
// returned inside MatchResponse, so the client never needs a second request to know
// where a match lives. See docs/08.
public record MatchPlacementRequest(string PhaseId, string RoundId, int SlotIndex);

public record MatchPlacementResponse(string PhaseId, string RoundId, int SlotIndex, Guid MatchId);
