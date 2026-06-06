namespace TournamentManager.Infrastructure.Encounters;

public static class TeamPairingSchedule
{
    public const int TeamSize = 3;

    public record BoutSpec(int BoutNumber, int APosition, int BPosition, int TargetCumulativeScore);

    public static IReadOnlyList<BoutSpec> Fie3v3 { get; } =
    [
        new(1, 3, 3, 5),
        new(2, 1, 2, 10),
        new(3, 2, 1, 15),
        new(4, 3, 2, 20),
        new(5, 1, 1, 25),
        new(6, 2, 3, 30),
        new(7, 3, 1, 35),
        new(8, 2, 2, 40),
        new(9, 1, 3, 45),
    ];
}
