using System.Text.RegularExpressions;
using TournamentManager.Domain.Format;
using YamlDotNet.RepresentationModel;

namespace TournamentManager.Infrastructure.Format;

public class TournamentFormatParser : ITournamentFormatParser
{
    private static readonly string[] SupportedVersions = ["0.1", "0.2"];
    private static readonly Regex PhaseIdPattern = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    public FormatParseResult Parse(string yaml)
    {
        var errors = new List<FormatError>();

        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0)
            {
                errors.Add(new FormatError("", "empty_document", "YAML document is empty"));
                return new FormatParseResult(false, null, errors);
            }
            if (stream.Documents[0].RootNode is not YamlMappingNode rootNode)
            {
                errors.Add(new FormatError("", "invalid_root", "Root document must be a YAML mapping"));
                return new FormatParseResult(false, null, errors);
            }
            root = rootNode;
        }
        catch (Exception ex)
        {
            errors.Add(new FormatError("", "yaml_parse_error", $"Failed to parse YAML: {ex.Message}"));
            return new FormatParseResult(false, null, errors);
        }

        var format = MapDocument(root, errors);

        if (errors.Count > 0)
            return new FormatParseResult(false, null, errors);

        ValidateSemantic(format!, errors);
        return new FormatParseResult(errors.Count == 0, errors.Count == 0 ? format : null, errors);
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
                var phase = MapPhase(phaseNode, phasePath, errors);
                if (phase != null) format.Phases.Add(phase);
            }
        }

        return format;
    }

    private static MatchDefaults MapDefaults(YamlMappingNode node, string path, List<FormatError> errors) =>
        new()
        {
            RoundDurationSeconds = GetOptionalInt(node, "roundDurationSeconds", path, errors, min: 1),
            RoundsPerMatch = GetOptionalInt(node, "roundsPerMatch", path, errors, min: 1),
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

    private static PhaseSpec? MapPhase(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var typeStr = GetRequiredString(node, "type", path, errors);
        if (typeStr == null) return null;

        return typeStr switch
        {
            "roundRobin" => MapRoundRobinPhase(node, path, errors),
            "singleElimination" => MapSingleEliminationPhase(node, path, errors),
            "doubleElimination" => MapDoubleEliminationPhase(node, path, errors),
            _ => Err<PhaseSpec>(errors, P(path, "type"), "unknown_phase_type",
                $"Unknown phase type '{typeStr}'; supported: roundRobin, singleElimination, doubleElimination"),
        };
    }

    private static RoundRobinPhase? MapRoundRobinPhase(YamlMappingNode node, string path, List<FormatError> errors)
    {
        var id = GetRequiredString(node, "id", path, errors);
        var name = GetRequiredString(node, "name", path, errors, maxLength: 200);

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

        var tieBreakers = new List<TieBreaker>();
        var tbSeq = GetRequiredSequence(node, "tieBreakers", path, errors);
        if (tbSeq != null)
        {
            for (var i = 0; i < tbSeq.Children.Count; i++)
            {
                var tbPath = $"{path}.tieBreakers[{i}]";
                if (tbSeq.Children[i] is not YamlScalarNode scalar)
                {
                    errors.Add(new FormatError(tbPath, "invalid_type", "Tie-breaker must be a string"));
                    continue;
                }
                TieBreaker? tb = scalar.Value switch
                {
                    "scoreDifference" => TieBreaker.ScoreDifference,
                    "random" => TieBreaker.Random,
                    _ => null,
                };
                if (tb == null)
                    errors.Add(new FormatError(tbPath, "invalid_value",
                        $"Unknown tie-breaker '{scalar.Value}'; supported: scoreDifference, random"));
                else
                    tieBreakers.Add(tb.Value);
            }
        }

        if (id == null || name == null || groups == null || points == null) return null;

        return new RoundRobinPhase
        {
            Id = id, Name = name,
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

            if (from != null)
                seeding = new SeedingSpec { From = from, Slots = slots };
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
                RoundsPerMatch = GetOptionalInt(ovNode, "roundsPerMatch", ovPath, errors, min: 1),
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
                "simple" => GrandFinalMode.Simple,
                "reset" => GrandFinalMode.Reset,
                _ => null,
            };
            if (grandFinal == null)
                errors.Add(new FormatError(P(path, "grandFinal"), "invalid_value",
                    $"'grandFinal' must be 'simple' or 'reset', got '{gfStr}'"));
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
                if (source != null && rank != null)
                    slots.Add(new SlotSpec { Source = source, Rank = rank.Value });
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

    // -------------------------------------------------------------------------
    // Semantic validation
    // -------------------------------------------------------------------------

    private static void ValidateSemantic(TournamentFormat format, List<FormatError> errors)
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
                    ValidateRoundRobin(rrp, i, errors);
                    break;
                case SingleEliminationPhase sep:
                    ValidateSingleElimination(sep, i, format.Phases.Take(i).ToList(), errors);
                    break;
                case DoubleEliminationPhase dep:
                    ValidateDoubleElimination(dep, i, format.Phases.Take(i).ToList(), errors);
                    break;
            }
        }
    }

    private static void ValidateRoundRobin(RoundRobinPhase phase, int idx, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";
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

    private static void ValidateSingleElimination(
        SingleEliminationPhase phase, int idx, List<PhaseSpec> priorPhases, List<FormatError> errors)
    {
        var path = $"phases[{idx}]";
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

            var effectiveRounds = phase.Rounds ?? GetSystemRounds(slotCount);
            var allRoundIds = effectiveRounds.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
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

        // Slot counts
        var ubSlotCount = ub.Slots.Count;
        var ubSlotsValid = ubSlotCount >= 2 && IsPowerOfTwo(ubSlotCount);
        if (!ubSlotsValid)
            errors.Add(new FormatError(P(path, "upperBracket.slots"), "invalid_slot_count",
                $"Upper bracket slot count ({ubSlotCount}) must be a power of 2 and ≥ 2"));

        var lbSlotCount = lb.Slots.Count;
        if (lbSlotCount != ubSlotCount)
            errors.Add(new FormatError(P(path, "lowerBracket.slots"), "invalid_slot_count",
                $"Lower bracket slot count ({lbSlotCount}) must equal upper bracket slot count ({ubSlotCount})"));

        // Round counts: UB = log2(slots), LB = 2 * log2(slots)
        if (ubSlotsValid)
        {
            var ubRoundCount = (int)Math.Log2(ubSlotCount);
            if (ub.Rounds.Count != ubRoundCount)
                errors.Add(new FormatError(P(path, "upperBracket.rounds"), "rounds_count_mismatch",
                    $"Upper bracket must have {ubRoundCount} rounds for {ubSlotCount} slots, got {ub.Rounds.Count}"));

            var expectedLbRounds = 2 * ubRoundCount;
            if (lb.Rounds.Count != expectedLbRounds)
                errors.Add(new FormatError(P(path, "lowerBracket.rounds"), "rounds_count_mismatch",
                    $"Lower bracket must have {expectedLbRounds} rounds for {ubSlotCount} upper slots, got {lb.Rounds.Count}"));
        }

        // Collect UB round IDs
        var ubRoundIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < ub.Rounds.Count; i++)
        {
            if (!ubRoundIds.Add(ub.Rounds[i].Id))
                errors.Add(new FormatError($"{path}.upperBracket.rounds[{i}].id", "duplicate_round_id",
                    $"Round id '{ub.Rounds[i].Id}' is not unique in upper bracket"));
        }

        // Collect LB round IDs, validate dropdownsFrom
        var lbRoundIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lb.Rounds.Count; i++)
        {
            var r = lb.Rounds[i];
            if (!lbRoundIds.Add(r.Id))
                errors.Add(new FormatError($"{path}.lowerBracket.rounds[{i}].id", "duplicate_round_id",
                    $"Round id '{r.Id}' is not unique in lower bracket"));

            if (r.DropdownsFrom != null && !ubRoundIds.Contains(r.DropdownsFrom))
                errors.Add(new FormatError($"{path}.lowerBracket.rounds[{i}].dropdownsFrom", "unknown_round_id",
                    $"dropdownsFrom '{r.DropdownsFrom}' does not reference a known upper bracket round id"));
        }

        // Cross-bracket round ID conflicts
        foreach (var id in ubRoundIds.Intersect(lbRoundIds))
            errors.Add(new FormatError(P(path, "rounds"), "duplicate_round_id",
                $"Round id '{id}' appears in both upper and lower bracket"));

        // Validate overrides
        var validOverrideIds = new HashSet<string>(ubRoundIds, StringComparer.Ordinal);
        validOverrideIds.UnionWith(lbRoundIds);
        validOverrideIds.Add("grandFinal");
        if (phase.GrandFinal == GrandFinalMode.Reset)
            validOverrideIds.Add("grandFinalReset");

        for (var i = 0; i < phase.Overrides.Count; i++)
        {
            if (!validOverrideIds.Contains(phase.Overrides[i].RoundId))
                errors.Add(new FormatError($"{path}.overrides[{i}].roundId", "unknown_round_id",
                    $"Override references unknown round id '{phase.Overrides[i].RoundId}'"));
        }

        // Validate slot seeding across both brackets (no duplicates across brackets)
        var allSlotPairs = new HashSet<(string, int)>();
        ValidateBracketSlots(ub.Slots, P(path, "upperBracket"), priorPhases, allSlotPairs, errors);
        ValidateBracketSlots(lb.Slots, P(path, "lowerBracket"), priorPhases, allSlotPairs, errors);
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

    private static List<RoundSpec> GetSystemRounds(int slotCount) => slotCount switch
    {
        2 => [new("final", "Final")],
        4 => [new("semiFinal", "Semi-final"), new("final", "Final")],
        8 => [new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        16 => [new("roundOf16", "Round of 16"), new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        32 => [new("roundOf32", "Round of 32"), new("roundOf16", "Round of 16"), new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        _ => [],
    };
}
