using TournamentManager.Domain.Enums;
using TournamentManager.Domain.Format;

namespace TournamentManager.Domain.Entities;

public class Tournament
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Nomination { get; set; }
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TournamentStatus Status { get; set; }

    public int? DefaultRoundDurationSeconds { get; set; }
    public int? DefaultMaxDoubles { get; set; }
    public int? DefaultMaxWarnings { get; set; }

    public TournamentFormat? Format { get; set; }
    public string? FormatYaml { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<TournamentParticipant> Participants { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
    public List<Document> Documents { get; set; } = new();
}
