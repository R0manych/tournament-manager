namespace Zettel.Domain.Entities;

public class TeamMember
{
    public Guid TeamId { get; set; }
    public Guid FighterId { get; set; }
    public int Position { get; set; }
    public DateTime AddedAt { get; set; }

    public Team Team { get; set; } = null!;
    public Fighter Fighter { get; set; } = null!;
}
