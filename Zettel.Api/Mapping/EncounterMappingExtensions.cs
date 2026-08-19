using Zettel.Api.Dto.Encounters;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;

namespace Zettel.Api.Mapping;

public static class EncounterMappingExtensions
{
    public static EncounterResponse ToResponse(this Encounter e, Tournament? t = null)
    {
        var contributingBouts = e.Bouts
            .Where(m => m.Status == MatchStatus.InProgress || m.Status == MatchStatus.Completed)
            .ToList();
        var score1 = contributingBouts.Sum(m => m.Score1);
        var score2 = contributingBouts.Sum(m => m.Score2);

        var basicBouts = e.Bouts.Where(m => m.BoutNumber is >= 1 and <= 9).ToList();
        var requiresTieBreak =
            basicBouts.Count == 9
            && basicBouts.All(m => m.Status == MatchStatus.Completed)
            && score1 == score2
            && e.Bouts.All(m => m.BoutNumber != 10);

        return new EncounterResponse(
            e.Id, e.TournamentId, e.Participant1Id, e.Participant2Id,
            e.ScheduledAt, e.Status.ToString(),
            e.TargetTotalScore, e.BoutDurationSeconds,
            score1, score2,
            e.WinnerParticipantId, e.PriorityParticipantId,
            requiresTieBreak,
            e.StartedAt, e.EndedAt, e.CreatedAt,
            e.Bouts
                .OrderBy(m => m.BoutNumber)
                .Select(m => m.ToResponse(t))
                .ToList());
    }
}
