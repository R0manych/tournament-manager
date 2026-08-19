using Zettel.Api.Dto.Teams;
using Zettel.Domain.Entities;

namespace Zettel.Api.Mapping;

public static class TeamMappingExtensions
{
    public static TeamResponse ToResponse(this Team team) => new(
        team.Id, team.TournamentId, team.Name, team.Club, team.City, team.CreatedAt,
        team.Members
            .OrderBy(m => m.Position)
            .Select(m => new TeamMemberResponse(
                m.FighterId,
                m.Fighter?.FirstName ?? "",
                m.Fighter?.LastName ?? "",
                m.Fighter?.Club,
                m.Position,
                m.AddedAt))
            .ToList());
}
