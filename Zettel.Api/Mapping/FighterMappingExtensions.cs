using Zettel.Api.Dto.Fighters;
using Zettel.Domain.Entities;

namespace Zettel.Api.Mapping;

public static class FighterMappingExtensions
{
    public static FighterResponse ToResponse(this Fighter f) => new(
        f.Id, f.FirstName, f.LastName, f.Club, f.CreatedAt);
}
