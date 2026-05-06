using System.Text.Json.Serialization;

namespace TournamentManager.Domain.Format;

public class TournamentFormat
{
    public string FormatVersion { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public MatchDefaults Defaults { get; set; } = new();
    public ParticipantsSpec Participants { get; set; } = null!;
    public List<PhaseSpec> Phases { get; set; } = new();
}

public class MatchDefaults
{
    public int? RoundDurationSeconds { get; set; }
    public int? RoundsPerMatch { get; set; }
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
public abstract class PhaseSpec
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class RoundRobinPhase : PhaseSpec
{
    public GroupsSpec Groups { get; set; } = null!;
    public PointsRule PointsPerMatch { get; set; } = null!;
    public List<TieBreaker> TieBreakers { get; set; } = new();
}

public class GroupsSpec
{
    public int Count { get; set; }
    public int Size { get; set; }
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
}

public class SlotSpec
{
    public string Source { get; set; } = null!;
    public int Rank { get; set; }
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
    public int? RoundsPerMatch { get; set; }
    public int? MaxDoubles { get; set; }
    public int? MaxWarnings { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrandFinalMode
{
    [JsonStringEnumMemberName("simple")] Simple,
    [JsonStringEnumMemberName("reset")] Reset,
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
