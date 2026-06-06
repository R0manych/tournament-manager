namespace TournamentManager.Domain.Entities;

public class Team
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string Name { get; set; } = null!;
    public string? Club { get; set; }
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
    public List<TeamMember> Members { get; set; } = new();
}
