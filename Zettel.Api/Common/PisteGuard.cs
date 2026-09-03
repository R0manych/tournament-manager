using Microsoft.EntityFrameworkCore;
using Zettel.Domain.Entities;
using Zettel.Domain.Enums;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Api.Common;

// Инварианты 53–56 (docs/09 §4) живут здесь, а не в контроллерах: назначение ристалища
// приходит с трёх сторон — PATCH /matches/{id}, PATCH /encounters/{id} и перевод в
// InProgress, — и разъехавшиеся проверки означали бы, что площадку можно занять дважды,
// зайдя не с той стороны.
public sealed record PisteError(int Status, string Title, string Detail);

public static class PisteGuard
{
    // Назначение говорит, где бой пойдёт; для прошедшего это бессмысленно и переписывало бы
    // историю (инвариант 55). Снятие назначения в терминальном статусе запрещено тем же
    // правилом: инвариант 58 разрешает его только до терминального.
    public static bool IsTerminal(MatchStatus status) =>
        status is MatchStatus.Completed
            or MatchStatus.Cancelled
            or MatchStatus.WalkoverWin
            or MatchStatus.DoubleLoss;

    public static async Task<PisteError?> ValidateMatchAssignmentAsync(
        TournamentDbContext db, Match match, Guid? pisteId, CancellationToken ct)
    {
        // Инвариант 54: ристалище боута — это ристалище его серии, и записать его боуту
        // напрямую нельзя, иначе боут сможет «уехать» с площадки своей серии.
        if (match.EncounterId is not null)
            return new(409, "Bout follows its encounter",
                "A bout has no piste of its own; assign the piste to its encounter " +
                $"(PATCH /encounters/{match.EncounterId}).");

        if (IsTerminal(match.Status))
            return new(409, "Match is finished",
                $"Match status is {match.Status}; a piste can only be assigned before the match is finished.");

        if (pisteId is null) return null;

        var error = await EnsureSameTournamentAsync(db, pisteId.Value, match.TournamentId, ct);
        if (error is not null) return error;

        // Идущий бой переехать на другую площадку может — но только на свободную, иначе
        // инвариант 56 обходится назначением вместо старта.
        return match.Status == MatchStatus.InProgress
            ? await EnsureFreeAsync(db, pisteId.Value, match.Id, null, ct)
            : null;
    }

    public static async Task<PisteError?> ValidateEncounterAssignmentAsync(
        TournamentDbContext db, Encounter encounter, Guid? pisteId, CancellationToken ct)
    {
        if (IsTerminal(encounter.Status))
            return new(409, "Encounter is finished",
                $"Encounter status is {encounter.Status}; a piste can only be assigned before the encounter is finished.");

        if (pisteId is null) return null;

        var error = await EnsureSameTournamentAsync(db, pisteId.Value, encounter.TournamentId, ct);
        if (error is not null) return error;

        // Серия занимает площадку и сама по себе, и любым идущим боутом.
        var running = encounter.Status == MatchStatus.InProgress
            || await db.Matches.AnyAsync(
                b => b.EncounterId == encounter.Id && b.Status == MatchStatus.InProgress, ct);

        return running
            ? await EnsureFreeAsync(db, pisteId.Value, null, encounter.Id, ct)
            : null;
    }

    // Инвариант 53: чужое ристалище не назначается. Турнир у ристалища неизменяем
    // (инвариант 52), поэтому одной проверки при назначении достаточно.
    private static async Task<PisteError?> EnsureSameTournamentAsync(
        TournamentDbContext db, Guid pisteId, Guid tournamentId, CancellationToken ct)
    {
        var owner = await db.Pistes.AsNoTracking()
            .Where(p => p.Id == pisteId)
            .Select(p => (Guid?)p.TournamentId)
            .FirstOrDefaultAsync(ct);

        if (owner is null)
            return new(400, "Unknown piste", $"Piste {pisteId} does not exist.");

        return owner == tournamentId
            ? null
            : new(400, "Piste belongs to another tournament",
                $"Piste {pisteId} belongs to tournament {owner}, not {tournamentId}.");
    }

    // Инвариант 56: на площадке одновременно не больше одной идущей встречи. Единица
    // занятости — не боут, а серия: девять боутов одной серии идут на одном ристалище
    // подряд, и запуск следующего не должен спотыкаться о предыдущий.
    public static async Task<PisteError?> EnsureFreeAsync(
        TournamentDbContext db, Guid pisteId, Guid? exceptMatchId, Guid? exceptEncounterId,
        CancellationToken ct)
    {
        var matches = db.Matches.AsNoTracking()
            .Where(m => m.Status == MatchStatus.InProgress)
            .Where(m => m.PisteId == pisteId || (m.Encounter != null && m.Encounter.PisteId == pisteId));

        if (exceptMatchId is not null)
            matches = matches.Where(m => m.Id != exceptMatchId.Value);
        if (exceptEncounterId is not null)
            matches = matches.Where(m => m.EncounterId != exceptEncounterId.Value);

        var busyMatch = await matches.Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        if (busyMatch is not null)
            return Busy($"match {busyMatch}");

        var encounters = db.Encounters.AsNoTracking()
            .Where(e => e.Status == MatchStatus.InProgress && e.PisteId == pisteId);

        if (exceptEncounterId is not null)
            encounters = encounters.Where(e => e.Id != exceptEncounterId.Value);

        var busyEncounter = await encounters.Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        return busyEncounter is null ? null : Busy($"encounter {busyEncounter}");

        PisteError Busy(string occupant) => new(409, "Piste is busy",
            $"Piste {pisteId} already has an InProgress {occupant}. " +
            "Finish or cancel it, or move one of the two to another piste.");
    }
}
