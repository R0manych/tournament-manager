namespace TournamentManager.Domain.Entities;

public class Fighter
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? Club { get; set; }
    public DateTime CreatedAt { get; set; }
}
