using System.Text.Json.Serialization;

namespace Zettel.Domain.Format;

public class TournamentFormat
{
    public string FormatVersion { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public ParticipantsKindSpec ParticipantsKind { get; set; } = ParticipantsKindSpec.Fighter;
    public MatchDefaults Defaults { get; set; } = new();
    public TeamSpec? Team { get; set; }
    public ParticipantsSpec Participants { get; set; } = null!;
    public List<PhaseSpec> Phases { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParticipantsKindSpec
{
    [JsonStringEnumMemberName("fighter")] Fighter,
    [JsonStringEnumMemberName("team")] Team,
}

public class TeamSpec
{
    public int Size { get; set; }
    public int? TargetTotalScore { get; set; }
    public int? BoutDurationSeconds { get; set; }
}

public class MatchDefaults
{
    public int? RoundDurationSeconds { get; set; }
    public int? MaxDoubles { get; set; }
    public int? MaxWarnings { get; set; }
}

public class ParticipantsSpec
{
    public int Count { get; set; }
    public SeedingMode Seeding { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SeedingMode
{
    [JsonStringEnumMemberName("ranked")] Ranked,
    [JsonStringEnumMemberName("random")] Random,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RoundRobinPhase), "roundRobin")]
[JsonDerivedType(typeof(SingleEliminationPhase), "singleElimination")]
[JsonDerivedType(typeof(DoubleEliminationPhase), "doubleElimination")]
[JsonDerivedType(typeof(SwissPhase), "swiss")]
public abstract class PhaseSpec
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class RoundRobinPhase : PhaseSpec
{
    public RoundRobinSeedingSpec? Seeding { get; set; }
    public GroupsSpec Groups { get; set; } = null!;
    public PointsRule PointsPerMatch { get; set; } = null!;
    public List<TieBreaker> TieBreakers { get; set; } = new();
}

public class GroupsSpec
{
    public int Count { get; set; }
    public int Size { get; set; }
}

public class RoundRobinSeedingSpec
{
    public string From { get; set; } = null!;
    public Dictionary<string, List<SlotSpec>> Groups { get; set; } = new();
}

public class PointsRule
{
    public int Win { get; set; }
    public int Draw { get; set; }
    public int Loss { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TieBreaker
{
    [JsonStringEnumMemberName("scoreDifference")] ScoreDifference,
    [JsonStringEnumMemberName("buchholz")] Buchholz,
    [JsonStringEnumMemberName("buchholzCut1")] BuchholzCut1,
    [JsonStringEnumMemberName("sonnebornBerger")] SonnebornBerger,
    [JsonStringEnumMemberName("opponentWinRate")] OpponentWinRate,
    [JsonStringEnumMemberName("cumulative")] Cumulative,
    [JsonStringEnumMemberName("random")] Random,
}

public class SingleEliminationPhase : PhaseSpec
{
    public SeedingSpec Seeding { get; set; } = null!;
    public List<RoundSpec>? Rounds { get; set; }
    public bool ThirdPlaceMatch { get; set; }
    public List<RoundOverride> Overrides { get; set; } = new();
}

public class SeedingSpec
{
    public string From { get; set; } = null!;
    public List<SlotSpec> Slots { get; set; } = new();
    public TakeSpec? Take { get; set; }
    public BracketSizing BracketSize { get; set; } = BracketSizing.Explicit;
}

public class TakeSpec
{
    public TakeMode Mode { get; set; }
    public List<int>? Ranks { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TakeMode
{
    [JsonStringEnumMemberName("qualified")] Qualified,
    [JsonStringEnumMemberName("ranks")] Ranks,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BracketSizing
{
    [JsonStringEnumMemberName("explicit")] Explicit,
    [JsonStringEnumMemberName("auto")] Auto,
}

public class SlotSpec
{
    public string Source { get; set; } = null!;
    public int Rank { get; set; }
    public string? EntersAt { get; set; }
}

public class RoundSpec(string id, string name)
{
    public string Id { get; set; } = id;
    public string Name { get; set; } = name;
}

public class RoundOverride
{
    public string RoundId { get; set; } = null!;
    public int? RoundDurationSeconds { get; set; }
    public int? MaxDoubles { get; set; }
    public int? MaxWarnings { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrandFinalMode
{
    [JsonStringEnumMemberName("simple")] Simple,
    [JsonStringEnumMemberName("reset")] Reset,
    [JsonStringEnumMemberName("advantage")] Advantage,
}

public class BracketRoundSpec(string id, string name)
{
    public string Id { get; set; } = id;
    public string Name { get; set; } = name;
    public string? DropdownsFrom { get; set; }
}

public class BracketSpec
{
    public List<SlotSpec> Slots { get; set; } = new();
    public List<BracketRoundSpec> Rounds { get; set; } = new();
}

public class DoubleEliminationPhase : PhaseSpec
{
    public GrandFinalMode GrandFinal { get; set; }
    public BracketSpec UpperBracket { get; set; } = null!;
    public BracketSpec LowerBracket { get; set; } = null!;
    public List<RoundOverride> Overrides { get; set; } = new();
}

public class SwissPhase : PhaseSpec
{
    public GroupsSpec? Groups { get; set; }
    public SeedingSpec? Seeding { get; set; }
    public int? Rounds { get; set; }
    public QualificationSpec? Qualification { get; set; }
    public PairingSpec Pairing { get; set; } = new();
    public PointsRule PointsPerMatch { get; set; } = null!;
    public List<TieBreaker> TieBreakers { get; set; } = new();
    public List<RoundOverride> Overrides { get; set; } = new();
}

public class QualificationSpec
{
    public int WinsToQualify { get; set; }
    public int LossesToEliminate { get; set; }
    public int? MaxRounds { get; set; }
}

public class PairingSpec
{
    public FirstRoundPairing FirstRound { get; set; } = FirstRoundPairing.Fold;
    public bool AvoidRematch { get; set; } = true;
    public FloatPolicy FloatPolicy { get; set; } = FloatPolicy.DownLowest;
    public ByePolicy ByePolicy { get; set; } = ByePolicy.LowestRank;
    public ByeResult ByeResult { get; set; } = ByeResult.Win;
    public PairingSystem System { get; set; } = PairingSystem.Dutch;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FirstRoundPairing
{
    [JsonStringEnumMemberName("fold")] Fold,
    [JsonStringEnumMemberName("adjacent")] Adjacent,
    [JsonStringEnumMemberName("random")] Random,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FloatPolicy
{
    [JsonStringEnumMemberName("downLowest")] DownLowest,
    [JsonStringEnumMemberName("downHighest")] DownHighest,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ByePolicy
{
    [JsonStringEnumMemberName("lowestRank")] LowestRank,
    [JsonStringEnumMemberName("highestRank")] HighestRank,
    [JsonStringEnumMemberName("random")] Random,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ByeResult
{
    [JsonStringEnumMemberName("win")] Win,
    [JsonStringEnumMemberName("draw")] Draw,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PairingSystem
{
    [JsonStringEnumMemberName("dutch")] Dutch,
    [JsonStringEnumMemberName("monrad")] Monrad,
}
