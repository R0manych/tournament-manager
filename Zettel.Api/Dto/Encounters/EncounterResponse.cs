using Zettel.Api.Dto.Matches;

namespace Zettel.Api.Dto.Encounters;

public record EncounterResponse(
    Guid Id,
    Guid TournamentId,
    Guid Participant1Id,
    Guid Participant2Id,
    DateTime? ScheduledAt,
    string Status,
    int TargetTotalScore,
    int BoutDurationSeconds,
    Guid? PisteId,
    string? PisteName,
    int Score1,
    int Score2,
    Guid? WinnerParticipantId,
    Guid? PriorityParticipantId,
    bool RequiresTieBreak,
    DateTime? StartedAt,
    DateTime? EndedAt,
    DateTime CreatedAt,
    List<MatchResponse> Bouts);
