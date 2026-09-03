namespace Zettel.Api.Dto.Matches;

public record PatchMatchRequest(
    DateTime? ScheduledAt,
    int? RoundDurationSeconds,
    int? MaxDoubles,
    int? MaxWarnings,
    Guid? PisteId);
