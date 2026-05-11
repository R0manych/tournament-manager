namespace TournamentManager.Api.Dto.Matches;

public record GenerateRoundRobinRequest(
    string PhaseId,
    List<List<Guid>> Groups);

public record GenerateRoundRobinResponse(
    int Created,
    int Skipped,
    List<MatchResponse> Matches);
