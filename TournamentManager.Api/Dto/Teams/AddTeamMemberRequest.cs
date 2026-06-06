using System.ComponentModel.DataAnnotations;

namespace TournamentManager.Api.Dto.Teams;

public record AddTeamMemberRequest(
    Guid FighterId,
    [Range(1, 3)] int Position);
