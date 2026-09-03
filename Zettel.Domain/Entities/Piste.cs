namespace Zettel.Domain.Entities;

// Площадка, на которой физически идут бои (docs/09). Своего турнира и только своего:
// глобального справочника нет по той же причине, что у команды (АР-11) — «ристалище 1»
// на двух турнирах это два разных объекта, и связывать их нечем.
public class Piste
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string Name { get; set; } = null!;
    public int OrderIndex { get; set; }
    public DateTime CreatedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
