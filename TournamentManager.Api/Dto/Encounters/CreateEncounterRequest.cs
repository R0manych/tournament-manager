using System.ComponentModel.DataAnnotations;

namespace TournamentManager.Api.Dto.Encounters;

public record CreateEncounterRequest(
    Guid Participant1Id,
    Guid Participant2Id,
    DateTime? ScheduledAt,
    [Range(1, int.MaxValue)] int? TargetTotalScore,
    [Range(1, int.MaxValue)] int? BoutDurationSeconds);
