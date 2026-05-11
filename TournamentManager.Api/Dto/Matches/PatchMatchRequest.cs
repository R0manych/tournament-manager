namespace TournamentManager.Api.Dto.Matches;

public record PatchMatchRequest(
    DateTime? ScheduledAt,
    int? RoundDurationSeconds,
    int? TotalRounds,
    int? MaxDoubles,
    int? MaxWarnings);
