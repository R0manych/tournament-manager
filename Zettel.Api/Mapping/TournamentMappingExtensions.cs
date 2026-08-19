using Zettel.Api.Dto.Tournaments;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;

namespace Zettel.Api.Mapping;

public static class TournamentMappingExtensions
{
    public static TournamentSummaryResponse ToSummaryResponse(this Tournament t) => new(
        t.Id, t.Name, t.Description, t.Nomination, t.Location,
        t.StartDate, t.EndDate, t.Status.ToString(),
        t.ParticipantKind.ToString(),
        t.DefaultRoundDurationSeconds,
        t.DefaultMaxDoubles, t.DefaultMaxWarnings,
        t.DefaultTeamTargetScore, t.DefaultTeamBoutDurationSeconds,
        t.CreatedAt,
        t.Participants.Count, t.Matches.Count);

    public static TournamentResponse ToDetailResponse(
        this Tournament t,
        ParticipantLookup? lookup = null) => new(
            t.Id, t.Name, t.Description, t.Nomination, t.Location,
            t.StartDate, t.EndDate, t.Status.ToString(),
            t.ParticipantKind.ToString(),
            t.DefaultRoundDurationSeconds,
            t.DefaultMaxDoubles, t.DefaultMaxWarnings,
            t.DefaultTeamTargetScore, t.DefaultTeamBoutDurationSeconds,
            t.CreatedAt,
            t.Participants.Select(p => p.ToResponse(t.ParticipantKind, lookup)).ToList(),
            t.Matches.Count);

    public static ParticipantResponse ToResponse(
        this TournamentParticipant p,
        ParticipantKind kind,
        ParticipantLookup? lookup)
    {
        if (kind == ParticipantKind.Fighter)
        {
            var info = lookup?.Fighters.GetValueOrDefault(p.ParticipantId);
            return new ParticipantResponse(
                p.ParticipantId, kind.ToString(),
                info is null ? null : new FighterParticipantInfo(info.FirstName, info.LastName, info.Club),
                null,
                p.Seed, p.RegisteredAt);
        }

        var team = lookup?.Teams.GetValueOrDefault(p.ParticipantId);
        return new ParticipantResponse(
            p.ParticipantId, kind.ToString(),
            null,
            team is null ? null : new TeamParticipantInfo(team.Name, team.Club, team.City),
            p.Seed, p.RegisteredAt);
    }
}

public class ParticipantLookup
{
    public Dictionary<Guid, Fighter> Fighters { get; } = new();
    public Dictionary<Guid, Team> Teams { get; } = new();
}
