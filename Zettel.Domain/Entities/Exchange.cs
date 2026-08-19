namespace Zettel.Domain.Entities;

public class Exchange
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public int Sequence { get; set; }
    public int RoundNumber { get; set; }
    public int Points1 { get; set; }
    public int Points2 { get; set; }
    public bool IsDoubleHit { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public Match Match { get; set; } = null!;
}
