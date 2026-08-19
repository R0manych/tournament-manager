namespace Zettel.Api.Dto.Groups;

// ParticipantIds is ordered (seed order within the group).
public record GroupItemDto(
    string Label,
    List<Guid> ParticipantIds);

// PhaseId now lives in the route. The field is kept optional so a client that
// still sends it is not silently ignored — a mismatch with the route is a 400.
public record SaveGroupsRequest(
    List<GroupItemDto> Groups,
    string? PhaseId = null);

public record GroupResponse(
    string PhaseId,
    string Label,
    List<Guid> ParticipantIds,
    DateTime UpdatedAt);
