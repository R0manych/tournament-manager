namespace TournamentManager.Api.Dto.Matches;

// Groups omitted → the server generates from the saved group composition
// (TournamentGroups) of this phase.
public record GenerateRoundRobinRequest(
    string PhaseId,
    List<List<Guid>>? Groups = null);

public record GenerateRoundRobinResponse(
    int Created,
    int Skipped,
    List<MatchResponse> Matches);
