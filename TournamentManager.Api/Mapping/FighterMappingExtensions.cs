using TournamentManager.Api.Dto.Fighters;
using TournamentManager.Domain.Entities;

namespace TournamentManager.Api.Mapping;

public static class FighterMappingExtensions
{
    public static FighterResponse ToResponse(this Fighter f) => new(
        f.Id, f.FirstName, f.LastName, f.Club, f.CreatedAt);
}
