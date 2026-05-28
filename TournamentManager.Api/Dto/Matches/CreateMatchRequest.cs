namespace TournamentManager.Api.Dto.Matches;

public record CreateMatchRequest(
    Guid Fighter1Id,
    Guid Fighter2Id,
    DateTime? ScheduledAt,
    int? RoundDurationSeconds,
    int? MaxDoubles,
    int? MaxWarnings);
