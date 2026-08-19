using TournamentManager.Domain.Format;

namespace TournamentManager.Infrastructure.Format;

// The set of round identifiers a phase can address. Two callers need the same answer:
// the parser, when it checks that an `overrides[].roundId` points at a real round, and
// the placement validation, when it checks MatchPlacement.RoundId (docs/08, §6).
// Keeping both on one implementation is the point — a round id that can be overridden
// is exactly a round id a match can be placed in.
public static class FormatRoundCatalog
{
    // Third-place match of a singleElimination phase. It is generated from the losers of
    // the semi-finals and has no entry in `rounds`, so before this id it could not be
    // addressed at all — neither by an override nor by a placement (docs/08, §6, ОВ-4).
    public const string ThirdPlaceRoundId = "thirdPlace";

    public static List<RoundSpec> SystemRounds(int slotCount) => slotCount switch
    {
        2 => [new("final", "Final")],
        4 => [new("semiFinal", "Semi-final"), new("final", "Final")],
        8 => [new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        16 => [new("roundOf16", "Round of 16"), new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        32 => [new("roundOf32", "Round of 32"), new("roundOf16", "Round of 16"), new("quarterFinal", "Quarter-final"), new("semiFinal", "Semi-final"), new("final", "Final")],
        _ => [],
    };

    public static HashSet<string> SingleEliminationRoundIds(SingleEliminationPhase phase)
    {
        var slotCount = phase.Seeding?.Slots.Count ?? 0;
        var rounds = phase.Rounds ?? SystemRounds(slotCount);
        var ids = rounds.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (phase.ThirdPlaceMatch) ids.Add(ThirdPlaceRoundId);
        return ids;
    }

    public static HashSet<string> DoubleEliminationRoundIds(DoubleEliminationPhase phase)
    {
        var ids = phase.UpperBracket.Rounds.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        ids.UnionWith(phase.LowerBracket.Rounds.Select(r => r.Id));
        ids.Add("grandFinal");
        if (phase.GrandFinal is GrandFinalMode.Reset or GrandFinalMode.Advantage)
            ids.Add("grandFinalReset");
        return ids;
    }

    public static HashSet<string> SwissRoundIds(SwissPhase phase)
    {
        var count = phase.Rounds
            ?? phase.Qualification?.MaxRounds
            ?? (phase.Qualification != null
                ? phase.Qualification.WinsToQualify + phase.Qualification.LossesToEliminate - 1
                : (int?)null);

        return count.HasValue
            ? Enumerable.Range(1, count.Value).Select(n => $"round{n}").ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    // Group labels are the "rounds" of a round-robin phase: the cell of a group-stage match
    // is (phase, group label, pair index) — see docs/08, §7. Labels are free text chosen by
    // the organiser, so the saved composition is authoritative; the A..Z fallback covers the
    // case where matches are generated from groups passed in the request body, before any
    // composition has been saved.
    public static HashSet<string> RoundRobinRoundIds(RoundRobinPhase phase, IEnumerable<string> savedGroupLabels)
    {
        var ids = savedGroupLabels.ToHashSet(StringComparer.Ordinal);
        if (ids.Count > 0) return ids;

        var count = Math.Min(phase.Groups?.Count ?? 0, 26);
        for (var i = 0; i < count; i++) ids.Add(((char)('A' + i)).ToString());
        return ids;
    }

    // The per-round override of match settings, if the phase declares one for this round.
    // Same pairing as above, read from the other end: a round id that can be overridden is a
    // round id a match can be placed in — so a placed match is exactly a match that knows
    // which override applies to it (ТЗ §5.3). Without a placement the round is unknowable:
    // Match carries no stage of its own (инвариант 23).
    //
    // roundRobin declares no overrides at all; the group stage has no round to single out.
    // The parser does not reject two overrides for the same round, so document order decides
    // and the first one wins.
    public static RoundOverride? OverrideFor(TournamentFormat? format, string phaseId, string roundId)
    {
        List<RoundOverride>? overrides = format?.Phases.FirstOrDefault(p => p.Id == phaseId) switch
        {
            SingleEliminationPhase se => se.Overrides,
            DoubleEliminationPhase de => de.Overrides,
            SwissPhase sw => sw.Overrides,
            _ => null,
        };

        return overrides?.FirstOrDefault(o => o.RoundId == roundId);
    }

    // Round ids of any phase. savedGroupLabels is only consulted for roundRobin phases;
    // pass an empty sequence when the caller has no composition at hand.
    public static HashSet<string>? RoundIdsOf(
        TournamentFormat format, string phaseId, IEnumerable<string> savedGroupLabels)
    {
        var phase = format.Phases.FirstOrDefault(p => p.Id == phaseId);
        return phase switch
        {
            RoundRobinPhase rr => RoundRobinRoundIds(rr, savedGroupLabels),
            SingleEliminationPhase se => SingleEliminationRoundIds(se),
            DoubleEliminationPhase de => DoubleEliminationRoundIds(de),
            SwissPhase sw => SwissRoundIds(sw),
            _ => null,
        };
    }
}
