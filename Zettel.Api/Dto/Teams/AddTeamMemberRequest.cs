using System.ComponentModel.DataAnnotations;

namespace Zettel.Api.Dto.Teams;

public record AddTeamMemberRequest(
    Guid FighterId,
    [Range(1, 3)] int Position);
