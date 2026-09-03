namespace Zettel.Api.Dto.Encounters;

// Единственное, что правится частичным обновлением серии, — её ристалище. Отсутствие поля
// и явный null означают одно и то же: снять назначение (docs/09 §5.2).
public record PatchEncounterRequest(Guid? PisteId);
