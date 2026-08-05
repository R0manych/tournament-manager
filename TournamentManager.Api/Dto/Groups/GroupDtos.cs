namespace TournamentManager.Api.Dto.Groups;

// ParticipantIds is ordered (seed order within the group).
public record GroupItemDto(
    string Label,
    List<Guid> ParticipantIds);

public record SaveGroupsRequest(
    string PhaseId,
    List<GroupItemDto> Groups);

public record GroupResponse(
    string PhaseId,
    string Label,
    List<Guid> ParticipantIds,
    DateTime UpdatedAt);
