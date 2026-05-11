using TournamentManager.Api.Dto.Tournaments;
using TournamentManager.Domain.Entities;

namespace TournamentManager.Api.Mapping;

public static class TournamentMappingExtensions
{
    public static TournamentSummaryResponse ToSummaryResponse(this Tournament t) => new(
        t.Id, t.Name, t.Description, t.Nomination, t.Location,
        t.StartDate, t.EndDate, t.Status.ToString(),
        t.DefaultRoundDurationSeconds, t.DefaultRoundsPerMatch,
        t.DefaultMaxDoubles, t.DefaultMaxWarnings,
        t.CreatedAt,
        t.Participants.Count, t.Matches.Count);

    public static TournamentResponse ToDetailResponse(this Tournament t) => new(
        t.Id, t.Name, t.Description, t.Nomination, t.Location,
        t.StartDate, t.EndDate, t.Status.ToString(),
        t.DefaultRoundDurationSeconds, t.DefaultRoundsPerMatch,
        t.DefaultMaxDoubles, t.DefaultMaxWarnings,
        t.CreatedAt,
        t.Participants.Select(p => p.ToResponse()).ToList(),
        t.Matches.Count);

    public static ParticipantResponse ToResponse(this TournamentParticipant p) => new(
        p.FighterId,
        p.Fighter.FirstName,
        p.Fighter.LastName,
        p.Fighter.Club,
        p.Seed,
        p.RegisteredAt);
}
