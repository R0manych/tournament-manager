namespace Zettel.Api.Dto.Pistes;

// CurrentMatchId / CurrentEncounterId едут в списке, чтобы UI не собирал занятость площадок
// из полного списка встреч турнира (docs/09 §5.1). CurrentEncounterId заполнен и когда серия
// уже идёт, а её очередной боут ещё не запущен: площадку она занимает и в этот момент.
public record PisteResponse(
    Guid Id,
    Guid TournamentId,
    string Name,
    int OrderIndex,
    DateTime CreatedAt,
    Guid? CurrentMatchId,
    Guid? CurrentEncounterId);

public record CreatePisteRequest(string Name, int? OrderIndex);

public record UpdatePisteRequest(string Name, int OrderIndex);
