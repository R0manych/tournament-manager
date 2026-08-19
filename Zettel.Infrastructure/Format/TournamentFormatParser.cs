using System.Text.RegularExpressions;
using Zettel.Domain.Format;
using YamlDotNet.RepresentationModel;

namespace Zettel.Infrastructure.Format;

public class TournamentFormatParser : ITournamentFormatParser
{
    private static readonly string[] SupportedVersions = ["0.1", "0.2", "0.3"];
    private const string SwissMinVersion = "0.3";
    private static readonly Regex PhaseIdPattern = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    private static readonly HashSet<TieBreaker> RoundRobinTieBreakers =
        [TieBreaker.ScoreDifference, TieBreaker.Random];
    private static readonly HashSet<TieBreaker> SwissTieBreakers =
        [TieBreaker.ScoreDifference, TieBreaker.Buchholz, TieBreaker.BuchholzCut1,
         TieBreaker.SonnebornBerger, TieBreaker.OpponentWinRate, TieBreaker.Cumulative,
         TieBreaker.Random];

    public FormatParseResult Parse(string yaml)
    {
        var errors = new List<FormatError>();
        var warnings = new List<FormatError>();

        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0)
            {
                errors.Add(new FormatError("", "empty_document", "YAML document is empty"));
                return new FormatParseResult(false, null, errors, warnings);
            }
            if (stream.Documents[0].RootNode is not YamlMappingNode rootNode)
            {
                errors.Add(new FormatError("", "invalid_root", "Root document must be a YAML mapping"));
                return new FormatParseResult(false, null, errors, warnings);
            }
            root = rootNode;
        }
        catch (Exception ex)
        {
            errors.Add(new FormatError("", "yaml_parse_error", $"Failed to parse YAML: {ex.Message}"));
            return new FormatParseResult(false, null, errors, warnings);
        }

        var format = MapDocument(root, errors);

        if (errors.Count > 0)
            return new FormatParseResult(false, null, errors, warnings);

        ValidateSemantic(format!, errors, warnings);
        return new FormatParseResult(errors.Count == 0, errors.Count == 0 ? format : null, errors, warnings);
    }

    // -------------------------------------------------------------------------
    // Mapping
    // -------------------------------------------------------------------------

    private static TournamentFormat MapDocument(YamlMappingNode root, List<FormatError> errors)
    {
        var format = new TournamentFormat();

        format.FormatVersion = GetRequiredString(root, "formatVersion", "", errors) ?? "";
        format.Name = GetRequiredString(root, "name", "", errors, maxLength: 200) ?? "";
        format.Description = GetOptionalString(root, "description", "", errors, maxLength: 4000);

        var defaultsNode = GetOptionalMapping(root, "defaults", "", errors);
        if (defaultsNode != null)
            format.Defaults = MapDefaults(defaultsNode, "defaults", errors);

        var participantsNode = GetRequiredMapping(root, "participants", "", errors);
        if (participantsNode != null)
            format.Participants = MapParticipants(participantsNode, "participants", errors) ?? null!;

        var phasesSeq = GetRequiredSequence(root, "phases", "", errors);
        if (phasesSeq != null)
        {
            for (var i = 0; i < phasesSeq.Children.Count; i++)
            {
                var phasePath = $"phases[{i}]";
                if (phasesSeq.Children[i] is not YamlMappingNode phaseNode)
                {
                    errors.Add(new FormatError(phasePath, "invalid_type", "Each phase must be a mapping"));
                    continue;
                }
                var phase = MapPhase(phaseNode, phasePath, format.FormatVersion, errors);
                if (phase != null) format.Phases.Add(phase);
            }
        }

        return format;
    }

    private static MatchDefaults MapDefaults(YamlMappingNode node, string path, List<FormatError> errors) =>
        new()
        {
            RoundDurationSeconds = GetOptionalInt(node, "roundDurationSeconds", path, errors, min: 1),
            MaxDoubles = GetOptionalInt(node, "maxDoubles", path, errors, min: 0),
            MaxWarnings = GetOptionalInt(node, "maxWarnings", path, errors, min: 0),
        };

    private static ParticipantsSpec? MapParticipants(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var count = GetRequiredInt(node, "count", path, errors, min: 2);
        var seedingStr = GetRequiredString(node, "seeding", path, errors);

        SeedingMode? seeding = null;
        if (seedingStr != null)
        {
            seeding = seedingStr switch
            {
                "ranked" => SeedingMode.Ranked,
                "random" => SeedingMode.Random,
                _ => null,
            };
            if (seeding == null)
                errors.Add(new FormatError(P(path, "seeding"), "invalid_value",
                    $"'seeding' must be 'ranked' or 'random', got '{seedingStr}'"));
        }

        if (count == null || seeding == null) return null;
        return new ParticipantsSpec { Count = count.Value, Seeding = seeding.Value };
    }

    private static PhaseSpec? MapPhase(YamlMappingNode node, string path, string formatVersion, List<FormatError> errors)
    {
        var typeStr = GetRequiredString(node, "type", path, errors);
        if (typeStr == null) return null;

        return typeStr switch
        {
            "roundRobin" => MapRoundRobinPhase(node, path, errors),
            "singleElimination" => MapSingleEliminationPhase(node, path, errors),
            "doubleElimination" => MapDoubleEliminationPhase(node, path, errors),
            "swiss" when IsAtLeast(formatVersion, SwissMinVersion) => MapSwissPhase(node, path, errors),
            "swiss" => Err<PhaseSpec>(errors, P(path, "type"), "unknown_phase_type",
                $"Phase type 'swiss' requires formatVersion ≥ {SwissMinVersion}; got '{formatVersion}'"),
            _ => Err<PhaseSpec>(errors, P(path, "type"), "unknown_phase_type",
                $"Unknown phase type '{typeStr}'; supported: roundRobin, singleElimination, doubleElimination, swiss"),
        };
    }

    private static RoundRobinPhase? MapRoundRobinPhase(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var id = GetRequiredString(node, "id", path, errors);
        var name = GetRequiredString(node, "name", path, errors, maxLength: 200);

        RoundRobinSeedingSpec? seeding = null;
        var seedingNode = GetOptionalMapping(node, "seeding", path, errors);
        if (seedingNode != null)
        {
            var sPath = P(path, "seeding");
            var from = GetRequiredString(seedingNode, "from", sPath, errors);
            var seedGroupsNode = GetRequiredMapping(seedingNode, "groups", sPath, errors);
            var groupsDict = new Dictionary<string, List<SlotSpec>>();

            if (seedGroupsNode != null)
            {
                foreach (var kvp in seedGroupsNode.Children)
                {
                    if (kvp.Key is not YamlScalarNode keyNode) continue;
                    var groupCode = keyNode.Value ?? "";
                    var groupPath = $"{sPath}.groups.{groupCode}";

                    if (kvp.Value is not YamlSequenceNode slotSeq)
                    {
                        errors.Add(new FormatError(groupPath, "invalid_type", $"Group '{groupCode}' slots must be a sequence"));
                        continue;
                    }

                    var slots = new List<SlotSpec>();
                    for (var i = 0; i < slotSeq.Children.Count; i++)
                    {
                        var slotPath = $"{groupPath}[{i}]";
                        if (slotSeq.Children[i] is not YamlMappingNode slotNode)
                        {
                            errors.Add(new FormatError(slotPath, "invalid_type", "Slot must be a mapping"));
                            continue;
                        }
                        var source = GetRequiredString(slotNode, "source", slotPath, errors);
                        var rank = GetRequiredInt(slotNode, "rank", slotPath, errors, min: 1);
                        if (source != null && rank != null)
                            slots.Add(new SlotSpec { Source = source, Rank = rank.Value });
                    }
                    groupsDict[groupCode] = slots;
                }
            }

            if (from != null)
                seeding = new RoundRobinSeedingSpec { From = from, Groups = groupsDict };
        }

        GroupsSpec? groups = null;
        var groupsNode = GetRequiredMapping(node, "groups", path, errors);
        if (groupsNode != null)
        {
            var gPath = P(path, "groups");
            var count = GetRequiredInt(groupsNode, "count", gPath, errors, min: 1, max: 26);
            var size = GetRequiredInt(groupsNode, "size", gPath, errors, min: 2);
            if (count != null && size != null)
                groups = new GroupsSpec { Count = count.Value, Size = size.Value };
        }

        PointsRule? points = null;
        var pointsNode = GetRequiredMapping(node, "pointsPerMatch", path, errors);
        if (pointsNode != null)
        {
            var pPath = P(path, "pointsPerMatch");
            var win = GetRequiredInt(pointsNode, "win", pPath, errors, min: 0);
            var draw = GetRequiredInt(pointsNode, "draw", pPath, errors, min: 0);
            var loss = GetRequiredInt(pointsNode, "loss", pPath, errors, min: 0);
            if (win != null && draw != null && loss != null)
                points = new PointsRule { Win = win.Value, Draw = draw.Value, Loss = loss.Value };
        }

        var tbSeq = GetRequiredSequence(node, "tieBreakers", path, errors);
        var tieBreakers = tbSeq != null
            ? MapTieBreakers(tbSeq, path, RoundRobinTieBreakers, errors)
            : new List<TieBreaker>();

        if (id == null || name == null || groups == null || points == null) return null;

        return new RoundRobinPhase
        {
            Id = id, Name = name,
            Seeding = seeding,
            Groups = groups, PointsPerMatch = points, TieBreakers = tieBreakers,
        };
    }

    private static SingleEliminationPhase? MapSingleEliminationPhase(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var id = GetRequiredString(node, "id", path, errors);
        var name = GetRequiredString(node, "name", path, errors, maxLength: 200);

        SeedingSpec? seeding = null;
        var seedingNode = GetRequiredMapping(node, "seeding", path, errors);
        if (seedingNode != null)
        {
            var sPath = P(path, "seeding");
            var from = GetRequiredString(seedingNode, "from", sPath, errors);
            var slots = new List<SlotSpec>();

            var slotsSeq = GetRequiredSequence(seedingNode, "slots", sPath, errors);
            if (slotsSeq != null)
            {
                for (var i = 0; i < slotsSeq.Children.Count; i++)
                {
                    var slotPath = $"{sPath}.slots[{i}]";
                    if (slotsSeq.Children[i] is not YamlMappingNode slotNode)
                    {
                        errors.Add(new FormatError(slotPath, "invalid_type", "Slot must be a mapping"));
                        continue;
                    }
                    var source = GetRequiredString(slotNode, "source", slotPath, errors);
                    var rank = GetRequiredInt(slotNode, "rank", slotPath, errors, min: 1);
                    if (source != null && rank != null)
                        slots.Add(new SlotSpec { Source = source, Rank = rank.Value });
                }
            }

            var take = MapTakeSpec(seedingNode, sPath, errors);
            var bracketSize = MapBracketSize(seedingNode, sPath, errors);

            if (from != null)
                seeding = new SeedingSpec { From = from, Slots = slots, Take = take, BracketSize = bracketSize };
        }

        List<RoundSpec>? rounds = null;
        var roundsSeq = GetOptionalSequence(node, "rounds", path, errors);
        if (roundsSeq != null)
        {
            rounds = new List<RoundSpec>();
            for (var i = 0; i < roundsSeq.Children.Count; i++)
            {
                var roundPath = $"{path}.rounds[{i}]";
                if (roundsSeq.Children[i] is not YamlMappingNode roundNode)
                {
                    errors.Add(new FormatError(roundPath, "invalid_type", "Round must be a mapping"));
                    continue;
                }
                var rid = GetRequiredString(roundNode, "id", roundPath, errors);
                var rname = GetRequiredString(roundNode, "name", roundPath, errors, maxLength: 200);
                if (rid != null && rname != null)
                    rounds.Add(new RoundSpec(rid, rname));
            }
        }

        var thirdPlace = GetOptionalBool(node, "thirdPlaceMatch", path, errors) ?? false;
        var overrides = MapOverrides(node, path, errors);

        if (id == null || name == null || seeding == null) return null;

        return new SingleEliminationPhase
        {
            Id = id, Name = name,
            Seeding = seeding, Rounds = rounds,
            ThirdPlaceMatch = thirdPlace, Overrides = overrides,
        };
    }

    private static List<RoundOverride> MapOverrides(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var overrides = new List<RoundOverride>();
        var seq = GetOptionalSequence(node, "overrides", path, errors);
        if (seq == null) return overrides;

        for (var i = 0; i < seq.Children.Count; i++)
        {
            var ovPath = $"{path}.overrides[{i}]";
            if (seq.Children[i] is not YamlMappingNode ovNode)
            {
                errors.Add(new FormatError(ovPath, "invalid_type", "Override must be a mapping"));
                continue;
            }
            var roundId = GetRequiredString(ovNode, "roundId", ovPath, errors);
            if (roundId == null) continue;
            overrides.Add(new RoundOverride
            {
                RoundId = roundId,
                RoundDurationSeconds = GetOptionalInt(ovNode, "roundDurationSeconds", ovPath, errors, min: 1),
                MaxDoubles = GetOptionalInt(ovNode, "maxDoubles", ovPath, errors, min: 0),
                MaxWarnings = GetOptionalInt(ovNode, "maxWarnings", ovPath, errors, min: 0),
            });
        }
        return overrides;
    }

    private static DoubleEliminationPhase? MapDoubleEliminationPhase(
        YamlMappingNode node, string path, List<FormatError> errors)
    {
        var id = GetRequiredString(node, "id", path, errors);
        var name = GetRequiredString(node, "name", path, errors, maxLength: 200);

        GrandFinalMode? grandFinal = null;
        var gfStr = GetRequiredString(node, "grandFinal", path, errors);
        if (gfStr != null)
        {
            grandFinal = gfStr switch
            {
                "simple"    => GrandFinalMode.Simple,
                "reset"     => GrandFinalMode.Reset,
                "advantage" => GrandFinalMode.Advantage,
                _ => null,
            };
            if (grandFinal == null)
                errors.Add(new FormatError(P(path, "grandFinal"), "invalid_value",
                    $"'grandFinal' must be 'simple', 'reset', or 'advantage', got '{gfStr}'"));
        }

        var ubNode = GetRequiredMapping(node, "upperBracket", path, errors);
        var ubBracket = ubNode != null ? MapBracketSpec(ubNode, P(path, "upperBracket"), errors) : null;

        var lbNode = GetRequiredMapping(node, "lowerBracket", path, errors);
        var lbBracket = lbNode != null ? MapBracketSpec(lbNode, P(path, "lowerBracket"), errors) : null;

        var overrides = MapOverrides(node, path, errors);

        if (id == null || name == null || grandFinal == null || ubBracket == null || lbBracket == null) return null;

        return new DoubleEliminationPhase
        {
            Id = id, Name = name,
            GrandFinal = grandFinal.Value,
            UpperBracket = ubBracket,
            LowerBracket = lbBracket,
            Overrides = overrides,
        };
    }

    private static BracketSpec MapBracketSpec(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var slots = new List<SlotSpec>();
        var slotsSeq = GetRequiredSequence(node, "slots", path, errors);
        if (slotsSeq != null)
        {
            for (var i = 0; i < slotsSeq.Children.Count; i++)
            {
                var slotPath = $"{path}.slots[{i}]";
                if (slotsSeq.Children[i] is not YamlMappingNode slotNode)
                {
                    errors.Add(new FormatError(slotPath, "invalid_type", "Slot must be a mapping"));
                    continue;
                }
                var source = GetRequiredString(slotNode, "source", slotPath, errors);
                var rank = GetRequiredInt(slotNode, "rank", slotPath, errors, min: 1);
                var entersAt = GetOptionalString(slotNode, "entersAt", slotPath, errors);
                if (source != null && rank != null)
                    slots.Add(new SlotSpec { Source = source, Rank = rank.Value, EntersAt = entersAt });
            }
        }

        var rounds = new List<BracketRoundSpec>();
        var roundsSeq = GetRequiredSequence(node, "rounds", path, errors);
        if (roundsSeq != null)
        {
            for (var i = 0; i < roundsSeq.Children.Count; i++)
            {
                var roundPath = $"{path}.rounds[{i}]";
                if (roundsSeq.Children[i] is not YamlMappingNode roundNode)
                {
                    errors.Add(new FormatError(roundPath, "invalid_type", "Round must be a mapping"));
                    continue;
                }
                var rid = GetRequiredString(roundNode, "id", roundPath, errors);
                var rname = GetRequiredString(roundNode, "name", roundPath, errors, maxLength: 200);
                var dropdownsFrom = GetOptionalString(roundNode, "dropdownsFrom", roundPath, errors);
                if (rid != null && rname != null)
                    rounds.Add(new BracketRoundSpec(rid, rname) { DropdownsFrom = dropdownsFrom });
            }
        }

        return new BracketSpec { Slots = slots, Rounds = rounds };
    }

    private static SwissPhase? MapSwissPhase(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var id = GetRequiredString(node, "id", path, errors);
        var name = GetRequiredString(node, "name", path, errors, maxLength: 200);

        GroupsSpec? groups = null;
        var groupsNode = GetOptionalMapping(node, "groups", path, errors);
        if (groupsNode != null)
        {
            var gPath = P(path, "groups");
            var count = GetRequiredInt(groupsNode, "count", gPath, errors, min: 1, max: 26);
            var size = GetRequiredInt(groupsNode, "size", gPath, errors, min: 2);
            if (count != null && size != null)
                groups = new GroupsSpec { Count = count.Value, Size = size.Value };
        }

        SeedingSpec? seeding = null;
        var seedingNode = GetOptionalMapping(node, "seeding", path, errors);
        if (seedingNode != null)
        {
            var sPath = P(path, "seeding");
            var from = GetRequiredString(seedingNode, "from", sPath, errors);

            var slots = new List<SlotSpec>();
            var slotsSeq = GetOptionalSequence(seedingNode, "slots", sPath, errors);
            if (slotsSeq != null)
            {
                for (var i = 0; i < slotsSeq.Children.Count; i++)
                {
                    var slotPath = $"{sPath}.slots[{i}]";
                    if (slotsSeq.Children[i] is not YamlMappingNode slotNode)
                    {
                        errors.Add(new FormatError(slotPath, "invalid_type", "Slot must be a mapping"));
                        continue;
                    }
                    var source = GetRequiredString(slotNode, "source", slotPath, errors);
                    var rank = GetRequiredInt(slotNode, "rank", slotPath, errors, min: 1);
                    if (source != null && rank != null)
                        slots.Add(new SlotSpec { Source = source, Rank = rank.Value });
                }
            }

            var take = MapTakeSpec(seedingNode, sPath, errors);
            var bracketSize = MapBracketSize(seedingNode, sPath, errors);

            if (from != null)
                seeding = new SeedingSpec { From = from, Slots = slots, Take = take, BracketSize = bracketSize };
        }

        var rounds = GetOptionalInt(node, "rounds", path, errors, min: 1);

        QualificationSpec? qualification = null;
        var qualNode = GetOptionalMapping(node, "qualification", path, errors);
        if (qualNode != null)
        {
            var qPath = P(path, "qualification");
            var wins = GetRequiredInt(qualNode, "winsToQualify", qPath, errors, min: 1);
            var losses = GetRequiredInt(qualNode, "lossesToEliminate", qPath, errors, min: 1);
            var maxRounds = GetOptionalInt(qualNode, "maxRounds", qPath, errors, min: 1);
            if (wins != null && losses != null)
                qualification = new QualificationSpec
                {
                    WinsToQualify = wins.Value,
                    LossesToEliminate = losses.Value,
                    MaxRounds = maxRounds,
                };
        }

        var pairing = new PairingSpec();
        var pairingNode = GetOptionalMapping(node, "pairing", path, errors);
        if (pairingNode != null)
            pairing = MapPairingSpec(pairingNode, P(path, "pairing"), errors);

        PointsRule? points = null;
        var pointsNode = GetRequiredMapping(node, "pointsPerMatch", path, errors);
        if (pointsNode != null)
        {
            var pPath = P(path, "pointsPerMatch");
            var win = GetRequiredInt(pointsNode, "win", pPath, errors, min: 0);
            var draw = GetRequiredInt(pointsNode, "draw", pPath, errors, min: 0);
            var loss = GetRequiredInt(pointsNode, "loss", pPath, errors, min: 0);
            if (win != null && draw != null && loss != null)
                points = new PointsRule { Win = win.Value, Draw = draw.Value, Loss = loss.Value };
        }

        var tbSeq = GetRequiredSequence(node, "tieBreakers", path, errors);
        var tieBreakers = tbSeq != null
            ? MapTieBreakers(tbSeq, path, SwissTieBreakers, errors)
            : new List<TieBreaker>();

        var overrides = MapOverrides(node, path, errors);

        if (id == null || name == null || points == null) return null;

        return new SwissPhase
        {
            Id = id, Name = name,
            Groups = groups,
            Seeding = seeding,
            Rounds = rounds,
            Qualification = qualification,
            Pairing = pairing,
            PointsPerMatch = points,
            TieBreakers = tieBreakers,
            Overrides = overrides,
        };
    }

    private static PairingSpec MapPairingSpec(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var p = new PairingSpec();

        var first = GetOptionalString(node, "firstRound", path, errors);
        if (first != null)
        {
            p.FirstRound = first switch
            {
                "fold" => FirstRoundPairing.Fold,
                "adjacent" => FirstRoundPairing.Adjacent,
                "random" => FirstRoundPairing.Random,
                _ => p.FirstRound,
            };
            if (first is not "fold" and not "adjacent" and not "random")
                errors.Add(new FormatError(P(path, "firstRound"), "invalid_value",
                    $"'firstRound' must be 'fold', 'adjacent', or 'random'; got '{first}'"));
        }

        var avoid = GetOptionalBool(node, "avoidRematch", path, errors);
        if (avoid.HasValue) p.AvoidRematch = avoid.Value;

        var floatStr = GetOptionalString(node, "floatPolicy", path, errors);
        if (floatStr != null)
        {
            p.FloatPolicy = floatStr switch
            {
                "downLowest" => FloatPolicy.DownLowest,
                "downHighest" => FloatPolicy.DownHighest,
                _ => p.FloatPolicy,
            };
            if (floatStr is not "downLowest" and not "downHighest")
                errors.Add(new FormatError(P(path, "floatPolicy"), "invalid_value",
                    $"'floatPolicy' must be 'downLowest' or 'downHighest'; got '{floatStr}'"));
        }

        var byeStr = GetOptionalString(node, "byePolicy", path, errors);
        if (byeStr != null)
        {
            p.ByePolicy = byeStr switch
            {
                "lowestRank" => ByePolicy.LowestRank,
                "highestRank" => ByePolicy.HighestRank,
                "random" => ByePolicy.Random,
                _ => p.ByePolicy,
            };
            if (byeStr is not "lowestRank" and not "highestRank" and not "random")
                errors.Add(new FormatError(P(path, "byePolicy"), "invalid_value",
                    $"'byePolicy' must be 'lowestRank', 'highestRank', or 'random'; got '{byeStr}'"));
        }

        var byeRes = GetOptionalString(node, "byeResult", path, errors);
        if (byeRes != null)
        {
            p.ByeResult = byeRes switch
            {
                "win" => ByeResult.Win,
                "draw" => ByeResult.Draw,
                _ => p.ByeResult,
            };
            if (byeRes is not "win" and not "draw")
                errors.Add(new FormatError(P(path, "byeResult"), "invalid_value",
                    $"'byeResult' must be 'win' or 'draw'; got '{byeRes}'"));
        }

        var sys = GetOptionalString(node, "system", path, errors);
        if (sys != null)
        {
            p.System = sys switch
            {
                "dutch" => PairingSystem.Dutch,
                "monrad" => PairingSystem.Monrad,
                _ => p.System,
            };
            if (sys is not "dutch" and not "monrad")
                errors.Add(new FormatError(P(path, "system"), "invalid_value",
                    $"'system' must be 'dutch' or 'monrad'; got '{sys}'"));
        }

        return p;
    }

    private static TakeSpec? MapTakeSpec(YamlMappingNode seedingNode, string sPath, List<FormatError> errors)
    {
        if (!seedingNode.Children.TryGetValue(new YamlScalarNode("take"), out var val))
            return null;
        var path = P(sPath, "take");

        if (val is YamlScalarNode scalar)
        {
            if (scalar.Value == "qualified")
                return new TakeSpec { Mode = TakeMode.Qualified };
            errors.Add(new FormatError(path, "invalid_value",
                $"'take' scalar must be 'qualified'; got '{scalar.Value}'"));
            return null;
        }

        if (val is YamlMappingNode mapping)
        {
            if (!mapping.Children.TryGetValue(new YamlScalarNode("ranks"), out var ranksVal))
            {
                errors.Add(new FormatError(path, "invalid_value",
                    "'take' object must contain 'ranks' array"));
                return null;
            }
            if (ranksVal is not YamlSequenceNode ranksSeq)
            {
                errors.Add(new FormatError(P(path, "ranks"), "invalid_type", "'ranks' must be a sequence"));
                return null;
            }
            var ranks = new List<int>();
            for (var i = 0; i < ranksSeq.Children.Count; i++)
            {
                var rPath = $"{path}.ranks[{i}]";
                if (ranksSeq.Children[i] is not YamlScalarNode rs || !int.TryParse(rs.Value, out var n))
                {
                    errors.Add(new FormatError(rPath, "invalid_type", "rank must be an integer"));
                    continue;
                }
                if (n < 1)
                    errors.Add(new FormatError(rPath, "out_of_range", $"rank must be ≥ 1, got {n}"));
                else
                    ranks.Add(n);
            }
            return new TakeSpec { Mode = TakeMode.Ranks, Ranks = ranks };
        }

        errors.Add(new FormatError(path, "invalid_type",
            "'take' must be either scalar 'qualified' or mapping with 'ranks'"));
        return null;
    }

    private static BracketSizing MapBracketSize(YamlMappingNode seedingNode, string sPath, List<FormatError> errors)
    {
        if (!seedingNode.Children.TryGetValue(new YamlScalarNode("bracketSize"), out var val))
            return BracketSizing.Explicit;
        var path = P(sPath, "bracketSize");
        if (val is YamlScalarNode scalar && scalar.Value == "auto")
            return BracketSizing.Auto;
        errors.Add(new FormatError(path, "invalid_value",
            $"'bracketSize' must be 'auto' when present; got '{(val as YamlScalarNode)?.Value}'"));
        return BracketSizing.Explicit;
    }

    private static List<TieBreaker> MapTieBreakers(
        YamlSequenceNode tbSeq, string phasePath,
        HashSet<TieBreaker> allowedSet, List<FormatError> errors)
    {
        var allowedNames = string.Join(", ", allowedSet.Select(TieBreakerName));
        var result = new List<TieBreaker>();
        for (var i = 0; i < tbSeq.Children.Count; i++)
        {
            var tbPath = $"{phasePath}.tieBreakers[{i}]";
            if (tbSeq.Children[i] is not YamlScalarNode scalar)
            {
                errors.Add(new FormatError(tbPath, "invalid_type", "Tie-breaker must be a string"));
                continue;
            }
            var tb = ParseTieBreakerName(scalar.Value);
            if (tb == null || !allowedSet.Contains(tb.Value))
            {
                errors.Add(new FormatError(tbPath, "invalid_value",
                    $"Unknown or unsupported tie-breaker '{scalar.Value}'; supported here: {allowedNames}"));
                continue;
            }
            result.Add(tb.Value);
        }
        return result;
    }

    private static TieBreaker? ParseTieBreakerName(string? s) => s switch
    {
        "scoreDifference" => TieBreaker.ScoreDifference,
        "buchholz" => TieBreaker.Buchholz,
        "buchholzCut1" => TieBreaker.BuchholzCut1,
        "sonnebornBerger" => TieBreaker.SonnebornBerger,
        "opponentWinRate" => TieBreaker.OpponentWinRate,
        "cumulative" => TieBreaker.Cumulative,
        "random" => TieBreaker.Random,
        _ => null,
    };

    private static string TieBreakerName(TieBreaker tb) => tb switch
    {
        TieBreaker.ScoreDifference => "scoreDifference",
        TieBreaker.Buchholz => "buchholz",
        TieBreaker.BuchholzCut1 => "buchholzCut1",
        TieBreaker.SonnebornBerger => "sonnebornBerger",
        TieBreaker.OpponentWinRate => "opponentWinRate",
        TieBreaker.Cumulative => "cumulative",
        TieBreaker.Random => "random",
        _ => tb.ToString(),
    };

    private static bool IsAtLeast(string formatVersion, string minVersion) =>
        string.CompareOrdinal(formatVersion, minVersion) >= 0;

    // -------------------------------------------------------------------------
    // Semantic validation
    // -------------------------------------------------------------------------

    private static void ValidateSemantic(TournamentFormat format, List<FormatError> errors, List<FormatError> warnings)
    {
        if (!SupportedVersions.Contains(format.FormatVersion))
            errors.Add(new FormatError("formatVersion", "unsupported_version",
                $"Unsupported format version '{format.FormatVersion}'; supported: {string.Join(", ", SupportedVersions)}"));

        if (format.Phases.Count == 0)
        {
            errors.Add(new FormatError("phases", "empty", "'phases' must contain at least one element"));
            return;
        }

        var phaseIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < format.Phases.Count; i++)
        {
            var phase = format.Phases[i];
            var phasePath = $"phases[{i}]";

            if (!PhaseIdPattern.IsMatch(phase.Id))
                errors.Add(new FormatError(P(phasePath, "id"), "invalid_id",
                    $"Phase id '{phase.Id}' must match ^[a-zA-Z][a-zA-Z0-9_]*$"));

            if (!phaseIds.Add(phase.Id))
                errors.Add(new FormatError(P(phasePath, "id"), "duplicate_id",
                    $"Phase id '{phase.Id}' is not unique in document"));
        }

        for (var i = 0; i < format.Phases.Count; i++)
        {
            switch (format.Phases[i])
            {
                case RoundRobinPhase rrp:
                    ValidateRoundRobin(rrp, i, format.Participants?.Count, errors);
                    if (rrp.Seeding != null)
                        ValidateRoundRobinSeeding(rrp, i, format.Phases.Take(i).ToList(), errors);
                    break;
                case SingleEliminationPhase sep:
                    ValidateSingleElimination(sep, i, format.Phases.Take(i).ToList(), errors);
                    break;
                case DoubleEliminationPhase dep:
                    ValidateDoubleElimination(dep, i, format.Phases.Take(i).ToList(), errors);
                    break;
                case SwissPhase sp:
                    ValidateSwiss(sp, i, format.Phases.Take(i).ToList(), format.Participants?.Count, errors, warnings);
                    break;
            }
        }
    }

    private static void ValidateRoundRobin(RoundRobinPhase phase, int idx, int? participantsCount, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";

        // Phases with explicit seeding take their participants from a prior phase,
        // so their capacity doesn't need to match the root participants.count.
        if (phase.Seeding == null && participantsCount.HasValue)
        {
            var capacity = phase.Groups.Count * phase.Groups.Size;
            if (capacity != participantsCount.Value)
                errors.Add(new FormatError(P(path, "groups"), "groups_capacity_mismatch",
                    $"groups.count × groups.size = {phase.Groups.Count} × {phase.Groups.Size} = {capacity}, but participants.count = {participantsCount.Value}"));
        }

        var tbPath = P(path, "tieBreakers");

        if (phase.TieBreakers.Count == 0)
        {
            errors.Add(new FormatError(tbPath, "empty", "'tieBreakers' must have at least one element"));
            return;
        }

        var seen = new HashSet<TieBreaker>();
        for (var i = 0; i < phase.TieBreakers.Count; i++)
        {
            if (!seen.Add(phase.TieBreakers[i]))
                errors.Add(new FormatError($"{tbPath}[{i}]", "duplicate_tie_breaker",
                    $"Tie-breaker '{phase.TieBreakers[i]}' is used more than once"));
        }

        if (phase.TieBreakers[^1] != TieBreaker.Random)
            errors.Add(new FormatError(tbPath, "last_must_be_random",
                "Last tie-breaker must be 'random' to guarantee termination"));
    }

    private static void ValidateRoundRobinSeeding(
        RoundRobinPhase phase, int idx, List<PhaseSpec> priorPhases, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";
        var seeding = phase.Seeding!;
        var sPath = P(path, "seeding");

        var sourcePhase = priorPhases.FirstOrDefault(p => p.Id == seeding.From);
        if (sourcePhase == null)
        {
            errors.Add(new FormatError(P(sPath, "from"), "unknown_phase",
                $"Phase '{seeding.From}' not found among phases declared before this one"));
            return;
        }
        if (sourcePhase is not RoundRobinPhase sourceRR)
        {
            errors.Add(new FormatError(P(sPath, "from"), "invalid_source_type",
                $"Phase '{seeding.From}' must be of type roundRobin to be used as seeding source"));
            return;
        }

        var expectedGroupCodes = Enumerable.Range(0, phase.Groups.Count)
            .Select(i => ((char)('A' + i)).ToString())
            .ToHashSet(StringComparer.Ordinal);

        if (seeding.Groups.Count != phase.Groups.Count)
            errors.Add(new FormatError(P(sPath, "groups"), "groups_count_mismatch",
                $"seeding.groups has {seeding.Groups.Count} entries but groups.count = {phase.Groups.Count}"));

        foreach (var code in seeding.Groups.Keys.Where(k => !expectedGroupCodes.Contains(k)))
            errors.Add(new FormatError($"{sPath}.groups.{code}", "invalid_group_code",
                $"Group '{code}' is not a valid group code for this phase (valid: {string.Join(", ", expectedGroupCodes)})"));

        var validSourceGroupCodes = Enumerable.Range(0, sourceRR.Groups.Count)
            .Select(i => ((char)('A' + i)).ToString())
            .ToHashSet(StringComparer.Ordinal);

        var allPairs = new HashSet<(string, int)>();

        foreach (var (groupCode, slots) in seeding.Groups)
        {
            if (!expectedGroupCodes.Contains(groupCode)) continue;

            var groupPath = $"{sPath}.groups.{groupCode}";

            if (slots.Count != phase.Groups.Size)
                errors.Add(new FormatError(groupPath, "group_size_mismatch",
                    $"Group '{groupCode}' has {slots.Count} slots but groups.size = {phase.Groups.Size}"));

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var slotPath = $"{groupPath}[{i}]";

                var parts = slot.Source.Split('.', 2);
                if (parts.Length != 2)
                {
                    errors.Add(new FormatError(P(slotPath, "source"), "invalid_source_format",
                        $"Source '{slot.Source}' must have format '<phaseId>.<groupCode>'"));
                    continue;
                }

                if (parts[0] != seeding.From)
                    errors.Add(new FormatError(P(slotPath, "source"), "source_phase_mismatch",
                        $"Source phase '{parts[0]}' must match seeding.from = '{seeding.From}'"));

                if (!validSourceGroupCodes.Contains(parts[1]))
                    errors.Add(new FormatError(P(slotPath, "source"), "invalid_group_code",
                        $"Group '{parts[1]}' does not exist in phase '{sourceRR.Id}' (valid: {string.Join(", ", validSourceGroupCodes)})"));

                if (slot.Rank < 1 || slot.Rank > sourceRR.Groups.Size)
                    errors.Add(new FormatError(P(slotPath, "rank"), "rank_out_of_range",
                        $"rank={slot.Rank} is out of range [1..{sourceRR.Groups.Size}] for phase '{sourceRR.Id}'"));

                if (!allPairs.Add((slot.Source, slot.Rank)))
                    errors.Add(new FormatError(slotPath, "duplicate_slot",
                        $"Slot ({slot.Source}, rank={slot.Rank}) appears more than once across all seeding groups"));
            }
        }
    }

    private static void ValidateSingleElimination(
        SingleEliminationPhase phase, int idx, List<PhaseSpec> priorPhases, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";

        if (phase.Seeding.Take != null)
            errors.Add(new FormatError(P(path, "seeding.take"), "take_not_allowed",
                "'take' is only allowed in 'swiss' phases; use explicit 'slots' here"));
        if (phase.Seeding.BracketSize == BracketSizing.Auto)
            errors.Add(new FormatError(P(path, "seeding.bracketSize"), "auto_not_allowed",
                "'bracketSize: auto' is only allowed in 'swiss' phases"));

        var slotCount = phase.Seeding.Slots.Count;

        if (slotCount < 2 || !IsPowerOfTwo(slotCount))
        {
            errors.Add(new FormatError(P(path, "seeding.slots"), "invalid_slot_count",
                $"Number of slots ({slotCount}) must be a power of 2 and ≥ 2"));
        }

        var sourcePhase = priorPhases.FirstOrDefault(p => p.Id == phase.Seeding.From);
        if (sourcePhase == null)
        {
            errors.Add(new FormatError(P(path, "seeding.from"), "unknown_phase",
                $"Phase '{phase.Seeding.From}' not found among phases declared before this one"));
        }
        else if (sourcePhase is not RoundRobinPhase sourceRR)
        {
            errors.Add(new FormatError(P(path, "seeding.from"), "invalid_source_type",
                $"Phase '{phase.Seeding.From}' must be of type roundRobin to be used as seeding source"));
        }
        else
        {
            ValidateSlots(phase, idx, sourceRR, errors);
        }

        if (IsPowerOfTwo(slotCount) && slotCount >= 2)
        {
            var expectedRoundCount = (int)Math.Log2(slotCount);

            if (phase.Rounds != null)
            {
                if (phase.Rounds.Count != expectedRoundCount)
                    errors.Add(new FormatError(P(path, "rounds"), "rounds_count_mismatch",
                        $"'rounds' must have {expectedRoundCount} entries for {slotCount} slots, got {phase.Rounds.Count}"));

                var roundIds = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < phase.Rounds.Count; i++)
                {
                    if (!roundIds.Add(phase.Rounds[i].Id))
                        errors.Add(new FormatError($"{path}.rounds[{i}].id", "duplicate_round_id",
                            $"Round id '{phase.Rounds[i].Id}' is not unique within this phase"));
                }
            }

            // Includes the synthetic `thirdPlace` id when the phase plays a third-place
            // match — see FormatRoundCatalog.
            var allRoundIds = FormatRoundCatalog.SingleEliminationRoundIds(phase);
            for (var i = 0; i < phase.Overrides.Count; i++)
            {
                if (!allRoundIds.Contains(phase.Overrides[i].RoundId))
                    errors.Add(new FormatError($"{path}.overrides[{i}].roundId", "unknown_round_id",
                        $"Override references unknown round id '{phase.Overrides[i].RoundId}'"));
            }
        }
    }

    private static void ValidateDoubleElimination(
        DoubleEliminationPhase phase, int idx, List<PhaseSpec> priorPhases, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";
        var ub = phase.UpperBracket;
        var lb = phase.LowerBracket;

        // --- Collect UB round IDs (ordered list + set for lookup) ---
        var ubRoundIdList = new List<string>(ub.Rounds.Count);
        var ubRoundIdSet = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ub.Rounds.Count; i++)
        {
            var rid = ub.Rounds[i].Id;
            if (!ubRoundIdSet.Add(rid))
                errors.Add(new FormatError($"{path}.upperBracket.rounds[{i}].id", "duplicate_round_id",
                    $"Round id '{rid}' is not unique in upper bracket"));
            else
                ubRoundIdList.Add(rid);
        }

        // --- Collect LB round IDs ---
        var lbRoundIdSet = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lb.Rounds.Count; i++)
        {
            var rid = lb.Rounds[i].Id;
            if (!lbRoundIdSet.Add(rid))
                errors.Add(new FormatError($"{path}.lowerBracket.rounds[{i}].id", "duplicate_round_id",
                    $"Round id '{rid}' is not unique in lower bracket"));
        }

        // --- Cross-bracket round ID conflicts ---
        foreach (var id in ubRoundIdSet.Intersect(lbRoundIdSet))
            errors.Add(new FormatError(P(path, "rounds"), "duplicate_round_id",
                $"Round id '{id}' appears in both upper and lower bracket"));

        // --- Validate entersAt on UB slots ---
        var firstUbRoundId = ubRoundIdList.Count > 0 ? ubRoundIdList[0] : null;
        for (var i = 0; i < ub.Slots.Count; i++)
        {
            var slot = ub.Slots[i];
            if (slot.EntersAt == null) continue;
            var slotPath = $"{path}.upperBracket.slots[{i}]";
            if (!ubRoundIdSet.Contains(slot.EntersAt))
                errors.Add(new FormatError($"{slotPath}.entersAt", "unknown_round_id",
                    $"entersAt '{slot.EntersAt}' does not reference a known upper bracket round id"));
            else if (slot.EntersAt == firstUbRoundId)
                errors.Add(new FormatError($"{slotPath}.entersAt", "enters_at_first_round",
                    $"entersAt cannot reference the first round '{firstUbRoundId}' — a bye into the first round is meaningless"));
        }

        // --- UB round-by-round balance; compute match count (= losers) per UB round ---
        // Slots without entersAt enter round 1; slots with entersAt enter the named round (bye).
        var ubMatchesPerRound = new Dictionary<string, int>(StringComparer.Ordinal);
        if (ubRoundIdList.Count > 0)
        {
            // Count byes entering each named round
            var byesByRound = new Dictionary<string, int>(StringComparer.Ordinal);
            var directCount = 0;
            foreach (var slot in ub.Slots)
            {
                if (slot.EntersAt == null)
                    directCount++;
                else if (ubRoundIdSet.Contains(slot.EntersAt) && slot.EntersAt != firstUbRoundId)
                    byesByRound[slot.EntersAt] = byesByRound.GetValueOrDefault(slot.EntersAt, 0) + 1;
            }

            if (directCount % 2 != 0)
                errors.Add(new FormatError($"{path}.upperBracket.slots", "invalid_slot_count",
                    $"Upper bracket has {directCount} direct (first-round) slots — must be even"));

            var prevWinners = directCount / 2;
            ubMatchesPerRound[ubRoundIdList[0]] = prevWinners;

            for (var i = 1; i < ubRoundIdList.Count; i++)
            {
                var roundId = ubRoundIdList[i];
                var byes = byesByRound.GetValueOrDefault(roundId, 0);

                if (byes > 0 && byes != prevWinners)
                    errors.Add(new FormatError($"{path}.upperBracket.rounds[{i}]", "bye_carryover_mismatch",
                        $"Upper bracket round '{roundId}': {byes} bye slot(s) vs {prevWinners} carry-over winner(s) from previous round — must be equal for 1-to-1 pairing"));

                var total = prevWinners + byes;
                if (total % 2 != 0)
                    errors.Add(new FormatError($"{path}.upperBracket.rounds[{i}]", "invalid_slot_count",
                        $"Upper bracket round '{roundId}': {prevWinners} carry-overs + {byes} byes = {total} — must be even"));
                prevWinners = total / 2;
                ubMatchesPerRound[roundId] = prevWinners;
            }
        }

        // --- LB: validate dropdownsFrom references + round-by-round balance ---
        // Each LB round's participant count = direct slots (first round only) + prev winners + losers from UB dropdown.
        // Losers from a UB round = matches in that UB round = ubMatchesPerRound[roundId].
        var usedDropdownRounds = new HashSet<string>(StringComparer.Ordinal);
        {
            var prevWinners = 0;
            for (var i = 0; i < lb.Rounds.Count; i++)
            {
                var round = lb.Rounds[i];
                var roundPath = $"{path}.lowerBracket.rounds[{i}]";
                var direct = (i == 0) ? lb.Slots.Count : 0;
                var dropdown = 0;

                if (round.DropdownsFrom != null)
                {
                    if (!ubRoundIdSet.Contains(round.DropdownsFrom))
                        errors.Add(new FormatError($"{roundPath}.dropdownsFrom", "unknown_round_id",
                            $"dropdownsFrom '{round.DropdownsFrom}' does not reference a known upper bracket round id"));
                    else if (!usedDropdownRounds.Add(round.DropdownsFrom))
                        errors.Add(new FormatError($"{roundPath}.dropdownsFrom", "duplicate_dropdown",
                            $"Upper bracket round '{round.DropdownsFrom}' is already used as dropdownsFrom in another lower bracket round"));
                    else if (ubMatchesPerRound.TryGetValue(round.DropdownsFrom, out var ubMatches))
                        dropdown = ubMatches;
                }

                var total = direct + prevWinners + dropdown;
                if (total % 2 != 0)
                    errors.Add(new FormatError(roundPath, "invalid_slot_count",
                        $"Lower bracket round '{round.Id}': {direct} direct + {prevWinners} carry-overs + {dropdown} dropdowns = {total} — must be even"));
                prevWinners = total / 2;
            }
        }

        // --- Validate slot source / rank / cross-bracket duplicates (source phase, group codes, rank range) ---
        var allSlotPairs = new HashSet<(string, int)>();
        ValidateBracketSlots(ub.Slots, P(path, "upperBracket"), priorPhases, allSlotPairs, errors);
        ValidateBracketSlots(lb.Slots, P(path, "lowerBracket"), priorPhases, allSlotPairs, errors);

        // --- Validate overrides ---
        var validOverrideIds = new HashSet<string>(ubRoundIdSet, StringComparer.Ordinal);
        validOverrideIds.UnionWith(lbRoundIdSet);
        validOverrideIds.Add("grandFinal");
        if (phase.GrandFinal is GrandFinalMode.Reset or GrandFinalMode.Advantage)
            validOverrideIds.Add("grandFinalReset");

        for (var i = 0; i < phase.Overrides.Count; i++)
        {
            if (!validOverrideIds.Contains(phase.Overrides[i].RoundId))
                errors.Add(new FormatError($"{path}.overrides[{i}].roundId", "unknown_round_id",
                    $"Override references unknown round id '{phase.Overrides[i].RoundId}'"));
        }
    }

    private static void ValidateBracketSlots(
        List<SlotSpec> slots, string bracketPath, List<PhaseSpec> priorPhases,
        HashSet<(string, int)> allSlotPairs, List<FormatError> errors)
    {
        var localPairs = new HashSet<(string, int)>();

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var slotPath = $"{bracketPath}.slots[{i}]";

            var parts = slot.Source.Split('.', 2);
            if (parts.Length != 2)
            {
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_source_format",
                    $"Source '{slot.Source}' must have format '<phaseId>.<groupCode>'"));
                continue;
            }

            var (phaseId, groupCode) = (parts[0], parts[1]);

            var sourcePhase = priorPhases.FirstOrDefault(p => p.Id == phaseId);
            if (sourcePhase == null)
            {
                errors.Add(new FormatError(P(slotPath, "source"), "unknown_phase",
                    $"Phase '{phaseId}' not found among phases declared before this one"));
                continue;
            }

            if (sourcePhase is not RoundRobinPhase sourceRR)
            {
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_source_type",
                    $"Phase '{phaseId}' must be of type roundRobin to be used as a seeding source"));
                continue;
            }

            var validGroupCodes = Enumerable.Range(0, sourceRR.Groups.Count)
                .Select(j => ((char)('A' + j)).ToString())
                .ToHashSet(StringComparer.Ordinal);

            if (!validGroupCodes.Contains(groupCode))
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_group_code",
                    $"Group '{groupCode}' does not exist in phase '{sourceRR.Id}' (valid: {string.Join(", ", validGroupCodes)})"));

            if (slot.Rank < 1 || slot.Rank > sourceRR.Groups.Size)
                errors.Add(new FormatError(P(slotPath, "rank"), "rank_out_of_range",
                    $"rank={slot.Rank} is out of range [1..{sourceRR.Groups.Size}] for phase '{sourceRR.Id}'"));

            var pair = (slot.Source, slot.Rank);
            if (!localPairs.Add(pair))
                errors.Add(new FormatError(slotPath, "duplicate_slot",
                    $"Slot ({slot.Source}, rank={slot.Rank}) is duplicated within this bracket"));
            else if (!allSlotPairs.Add(pair))
                errors.Add(new FormatError(slotPath, "duplicate_slot",
                    $"Slot ({slot.Source}, rank={slot.Rank}) is already used in the other bracket"));
        }
    }

    private static void ValidateSlots(
        SingleEliminationPhase phase, int idx, RoundRobinPhase sourceRR, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";
        var validGroupCodes = Enumerable.Range(0, sourceRR.Groups.Count)
            .Select(i => ((char)('A' + i)).ToString())
            .ToHashSet(StringComparer.Ordinal);

        var usedPairs = new HashSet<(string source, int rank)>();

        for (var i = 0; i < phase.Seeding.Slots.Count; i++)
        {
            var slot = phase.Seeding.Slots[i];
            var slotPath = $"{path}.seeding.slots[{i}]";

            var parts = slot.Source.Split('.', 2);
            if (parts.Length != 2)
            {
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_source_format",
                    $"Source '{slot.Source}' must have format '<phaseId>.<groupCode>'"));
                continue;
            }

            if (parts[0] != phase.Seeding.From)
                errors.Add(new FormatError(P(slotPath, "source"), "source_phase_mismatch",
                    $"Source phase '{parts[0]}' must match seeding.from = '{phase.Seeding.From}'"));

            if (!validGroupCodes.Contains(parts[1]))
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_group_code",
                    $"Group '{parts[1]}' does not exist in phase '{sourceRR.Id}' (valid: {string.Join(", ", validGroupCodes)})"));

            if (slot.Rank < 1 || slot.Rank > sourceRR.Groups.Size)
                errors.Add(new FormatError(P(slotPath, "rank"), "rank_out_of_range",
                    $"rank={slot.Rank} is out of range [1..{sourceRR.Groups.Size}] for phase '{sourceRR.Id}' (groups.size={sourceRR.Groups.Size})"));

            if (!usedPairs.Add((slot.Source, slot.Rank)))
                errors.Add(new FormatError(slotPath, "duplicate_slot",
                    $"Slot ({slot.Source}, rank={slot.Rank}) appears more than once"));
        }
    }

    private static void ValidateSwiss(
        SwissPhase phase, int idx, List<PhaseSpec> priorPhases, int? participantsCount,
        List<FormatError> errors, List<FormatError> warnings)
    {
        var path = $"phases[{idx}]";

        // ---- Tie-breakers ----
        var tbPath = P(path, "tieBreakers");
        if (phase.TieBreakers.Count == 0)
        {
            errors.Add(new FormatError(tbPath, "empty", "'tieBreakers' must have at least one element"));
        }
        else
        {
            var seenTb = new HashSet<TieBreaker>();
            for (var i = 0; i < phase.TieBreakers.Count; i++)
            {
                if (!seenTb.Add(phase.TieBreakers[i]))
                    errors.Add(new FormatError($"{tbPath}[{i}]", "duplicate_tie_breaker",
                        $"Tie-breaker '{TieBreakerName(phase.TieBreakers[i])}' is used more than once"));
            }
            if (phase.TieBreakers[^1] != TieBreaker.Random)
                warnings.Add(new FormatError(tbPath, "last_tie_breaker_should_be_random",
                    "Last tie-breaker should be 'random' to guarantee termination"));
        }

        // ---- Termination mode: exactly one of rounds / qualification ----
        var hasRounds = phase.Rounds.HasValue;
        var hasQual = phase.Qualification != null;
        if (!hasRounds && !hasQual)
            errors.Add(new FormatError(path, "swiss_termination_required",
                "swiss phase must specify exactly one of 'rounds' or 'qualification'"));
        else if (hasRounds && hasQual)
            errors.Add(new FormatError(path, "swiss_termination_required",
                "swiss phase cannot specify both 'rounds' and 'qualification'"));

        // ---- Seeding & expected pool size ----
        int? expectedPoolSize = null;
        PhaseSpec? sourcePhase = null;

        if (phase.Seeding == null)
        {
            if (idx != 0)
                errors.Add(new FormatError(path, "swiss_seeding_required",
                    "swiss phase that is not first must specify 'seeding' (source phase + slots/take)"));
            else if (participantsCount.HasValue)
                expectedPoolSize = participantsCount.Value;
        }
        else
        {
            var sPath = P(path, "seeding");
            sourcePhase = priorPhases.FirstOrDefault(p => p.Id == phase.Seeding.From);
            if (sourcePhase == null)
            {
                errors.Add(new FormatError(P(sPath, "from"), "unknown_phase",
                    $"Phase '{phase.Seeding.From}' not found among phases declared before this one"));
            }
            else
            {
                var hasSlots = phase.Seeding.Slots.Count > 0;
                var hasTake = phase.Seeding.Take != null;

                if (hasSlots && hasTake)
                    errors.Add(new FormatError(sPath, "seeding_take_slots_conflict",
                        "'seeding.take' and 'seeding.slots' are mutually exclusive"));
                else if (!hasSlots && !hasTake)
                    errors.Add(new FormatError(sPath, "seeding_source_required",
                        "swiss seeding requires either 'slots' or 'take'"));

                if (hasTake)
                {
                    var take = phase.Seeding.Take!;
                    if (take.Mode == TakeMode.Qualified)
                    {
                        if (sourcePhase is not SwissPhase swissSrc || swissSrc.Qualification == null)
                            errors.Add(new FormatError(P(sPath, "take"), "take_requires_qualification",
                                "'take: qualified' requires source phase to be 'swiss' in qualification mode"));
                        if (phase.Seeding.BracketSize != BracketSizing.Auto)
                            errors.Add(new FormatError(P(sPath, "bracketSize"), "auto_required",
                                "'take: qualified' requires 'bracketSize: auto' (number of qualified is not fixed)"));
                    }
                    else if (take.Mode == TakeMode.Ranks && take.Ranks != null)
                    {
                        if (sourcePhase is RoundRobinPhase rrSrc)
                        {
                            for (var i = 0; i < take.Ranks.Count; i++)
                            {
                                if (take.Ranks[i] < 1 || take.Ranks[i] > rrSrc.Groups.Size)
                                    errors.Add(new FormatError(
                                        $"{sPath}.take.ranks[{i}]", "rank_out_of_range",
                                        $"rank={take.Ranks[i]} is out of range [1..{rrSrc.Groups.Size}] for phase '{rrSrc.Id}'"));
                            }
                            expectedPoolSize = rrSrc.Groups.Count * take.Ranks.Count;
                        }
                        else if (sourcePhase is SwissPhase swissSrcRanks && swissSrcRanks.Qualification == null
                                 && swissSrcRanks.Groups != null)
                        {
                            for (var i = 0; i < take.Ranks.Count; i++)
                            {
                                if (take.Ranks[i] < 1 || take.Ranks[i] > swissSrcRanks.Groups.Size)
                                    errors.Add(new FormatError(
                                        $"{sPath}.take.ranks[{i}]", "rank_out_of_range",
                                        $"rank={take.Ranks[i]} is out of range [1..{swissSrcRanks.Groups.Size}] for phase '{swissSrcRanks.Id}'"));
                            }
                            expectedPoolSize = swissSrcRanks.Groups.Count * take.Ranks.Count;
                        }
                        else
                        {
                            errors.Add(new FormatError(P(sPath, "take"), "ranks_unsupported_source",
                                $"'take: ranks' requires source phase to have determinate ranks (roundRobin or swiss with fixed rounds and groups); source '{phase.Seeding.From}' does not"));
                        }
                    }
                }
                else if (hasSlots)
                {
                    expectedPoolSize = phase.Seeding.Slots.Count;
                    if (sourcePhase is RoundRobinPhase rrSlotSrc)
                        ValidateSwissSlotsAgainstRoundRobin(phase.Seeding, P(sPath, "slots"), rrSlotSrc, errors);
                    else
                        errors.Add(new FormatError(P(sPath, "from"), "invalid_source_type",
                            $"swiss 'seeding.slots' requires source phase '{phase.Seeding.From}' to be of type roundRobin"));
                }
            }
        }

        // ---- Pool / groups size check ----
        if (phase.Groups != null)
        {
            if (expectedPoolSize.HasValue)
            {
                var capacity = phase.Groups.Count * phase.Groups.Size;
                if (capacity != expectedPoolSize.Value)
                    errors.Add(new FormatError(P(path, "groups"), "pool_size_mismatch",
                        $"groups.count × groups.size = {phase.Groups.Count} × {phase.Groups.Size} = {capacity}, but pool source provides {expectedPoolSize.Value}"));
            }
        }

        // ---- rounds vs pool capacity (warning) ----
        if (hasRounds)
        {
            int? poolSize = phase.Groups?.Size ?? expectedPoolSize;
            if (poolSize.HasValue)
            {
                var maxRoundsByCapacity = poolSize.Value - 1;
                if (phase.Rounds!.Value > maxRoundsByCapacity)
                    warnings.Add(new FormatError(P(path, "rounds"), "swiss_rounds_exceed_capacity",
                        $"rounds={phase.Rounds} exceeds pool capacity (size − 1 = {maxRoundsByCapacity}); pair repeats unavoidable"));
            }
        }

        // ---- Overrides reference valid round ids (round1..roundN) ----
        int? effectiveRoundCount = phase.Rounds
            ?? phase.Qualification?.MaxRounds
            ?? (phase.Qualification != null
                ? phase.Qualification.WinsToQualify + phase.Qualification.LossesToEliminate - 1
                : (int?)null);

        if (effectiveRoundCount.HasValue && phase.Overrides.Count > 0)
        {
            var validRoundIds = Enumerable.Range(1, effectiveRoundCount.Value)
                .Select(n => $"round{n}")
                .ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < phase.Overrides.Count; i++)
            {
                if (!validRoundIds.Contains(phase.Overrides[i].RoundId))
                    errors.Add(new FormatError($"{path}.overrides[{i}].roundId", "unknown_round_id",
                        $"Override references unknown round id '{phase.Overrides[i].RoundId}' (expected one of round1..round{effectiveRoundCount})"));
            }
        }

        // NOTE: swiss_byes_required (warning when qualification tree forces byes) is deferred —
        // see spec §15.4 "Проверка чистоты отсечки". Implementing it requires simulating the
        // bucket tree for (size, winsToQualify, lossesToEliminate); not yet implemented.
    }

    private static void ValidateSwissSlotsAgainstRoundRobin(
        SeedingSpec seeding, string slotsPath, RoundRobinPhase sourceRR, List<FormatError> errors)
    {
        var validGroupCodes = Enumerable.Range(0, sourceRR.Groups.Count)
            .Select(i => ((char)('A' + i)).ToString())
            .ToHashSet(StringComparer.Ordinal);

        var usedPairs = new HashSet<(string source, int rank)>();
        for (var i = 0; i < seeding.Slots.Count; i++)
        {
            var slot = seeding.Slots[i];
            var slotPath = $"{slotsPath}[{i}]";

            var parts = slot.Source.Split('.', 2);
            if (parts.Length != 2)
            {
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_source_format",
                    $"Source '{slot.Source}' must have format '<phaseId>.<groupCode>'"));
                continue;
            }
            if (parts[0] != seeding.From)
                errors.Add(new FormatError(P(slotPath, "source"), "source_phase_mismatch",
                    $"Source phase '{parts[0]}' must match seeding.from = '{seeding.From}'"));
            if (!validGroupCodes.Contains(parts[1]))
                errors.Add(new FormatError(P(slotPath, "source"), "invalid_group_code",
                    $"Group '{parts[1]}' does not exist in phase '{sourceRR.Id}' (valid: {string.Join(", ", validGroupCodes)})"));
            if (slot.Rank < 1 || slot.Rank > sourceRR.Groups.Size)
                errors.Add(new FormatError(P(slotPath, "rank"), "rank_out_of_range",
                    $"rank={slot.Rank} is out of range [1..{sourceRR.Groups.Size}] for phase '{sourceRR.Id}'"));
            if (!usedPairs.Add((slot.Source, slot.Rank)))
                errors.Add(new FormatError(slotPath, "duplicate_slot",
                    $"Slot ({slot.Source}, rank={slot.Rank}) appears more than once"));
        }
    }

    // -------------------------------------------------------------------------
    // YAML node helpers
    // -------------------------------------------------------------------------

    private static string? GetRequiredString(YamlMappingNode node, string key, string basePath,
        List<FormatError> errors, int? maxLength = null)
    {
        var path = P(basePath, key);
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val))
        {
            errors.Add(new FormatError(path, "required", $"'{key}' is required"));
            return null;
        }
        if (val is not YamlScalarNode scalar)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a string"));
            return null;
        }
        var s = scalar.Value ?? "";
        if (maxLength.HasValue && s.Length > maxLength.Value)
            errors.Add(new FormatError(path, "too_long", $"'{key}' must be ≤ {maxLength} characters, got {s.Length}"));
        return s;
    }

    private static string? GetOptionalString(YamlMappingNode node, string key, string basePath,
        List<FormatError> errors, int? maxLength = null)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val)) return null;
        var path = P(basePath, key);
        if (val is not YamlScalarNode scalar)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a string"));
            return null;
        }
        var s = scalar.Value ?? "";
        if (maxLength.HasValue && s.Length > maxLength.Value)
            errors.Add(new FormatError(path, "too_long", $"'{key}' must be ≤ {maxLength} characters, got {s.Length}"));
        return s;
    }

    private static int? GetRequiredInt(YamlMappingNode node, string key, string basePath,
        List<FormatError> errors, int? min = null, int? max = null)
    {
        var path = P(basePath, key);
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val))
        {
            errors.Add(new FormatError(path, "required", $"'{key}' is required"));
            return null;
        }
        if (val is not YamlScalarNode scalar || !int.TryParse(scalar.Value, out var n))
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be an integer"));
            return null;
        }
        if (min.HasValue && n < min.Value)
            errors.Add(new FormatError(path, "out_of_range", $"'{key}' must be ≥ {min}, got {n}"));
        if (max.HasValue && n > max.Value)
            errors.Add(new FormatError(path, "out_of_range", $"'{key}' must be ≤ {max}, got {n}"));
        return n;
    }

    private static int? GetOptionalInt(YamlMappingNode node, string key, string basePath,
        List<FormatError> errors, int? min = null, int? max = null)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val)) return null;
        var path = P(basePath, key);
        if (val is not YamlScalarNode scalar || !int.TryParse(scalar.Value, out var n))
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be an integer"));
            return null;
        }
        if (min.HasValue && n < min.Value)
            errors.Add(new FormatError(path, "out_of_range", $"'{key}' must be ≥ {min}, got {n}"));
        if (max.HasValue && n > max.Value)
            errors.Add(new FormatError(path, "out_of_range", $"'{key}' must be ≤ {max}, got {n}"));
        return n;
    }

    private static bool? GetOptionalBool(YamlMappingNode node, string key, string basePath, List<FormatError> errors)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val)) return null;
        var path = P(basePath, key);
        if (val is not YamlScalarNode scalar)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a boolean"));
            return null;
        }
        return scalar.Value?.ToLowerInvariant() switch
        {
            "true" or "yes" => true,
            "false" or "no" => false,
            _ => null,
        };
    }

    private static YamlMappingNode? GetRequiredMapping(YamlMappingNode node, string key, string basePath, List<FormatError> errors)
    {
        var path = P(basePath, key);
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val))
        {
            errors.Add(new FormatError(path, "required", $"'{key}' is required"));
            return null;
        }
        if (val is not YamlMappingNode mapping)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a mapping"));
            return null;
        }
        return mapping;
    }

    private static YamlMappingNode? GetOptionalMapping(YamlMappingNode node, string key, string basePath, List<FormatError> errors)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val)) return null;
        var path = P(basePath, key);
        if (val is not YamlMappingNode mapping)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a mapping"));
            return null;
        }
        return mapping;
    }

    private static YamlSequenceNode? GetRequiredSequence(YamlMappingNode node, string key, string basePath, List<FormatError> errors)
    {
        var path = P(basePath, key);
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val))
        {
            errors.Add(new FormatError(path, "required", $"'{key}' is required"));
            return null;
        }
        if (val is not YamlSequenceNode seq)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a sequence"));
            return null;
        }
        return seq;
    }

    private static YamlSequenceNode? GetOptionalSequence(YamlMappingNode node, string key, string basePath, List<FormatError> errors)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out var val)) return null;
        var path = P(basePath, key);
        if (val is not YamlSequenceNode seq)
        {
            errors.Add(new FormatError(path, "invalid_type", $"'{key}' must be a sequence"));
            return null;
        }
        return seq;
    }

    // -------------------------------------------------------------------------
    // Utilities
    // -------------------------------------------------------------------------

    private static string P(string basePath, string key) =>
        basePath.Length == 0 ? key : $"{basePath}.{key}";

    private static T? Err<T>(List<FormatError> errors, string path, string code, string message) where T : class
    {
        errors.Add(new FormatError(path, code, message));
        return null;
    }

    private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

}
