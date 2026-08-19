namespace Zettel.Domain.Enums;

public enum TournamentStatus
{
    Draft,      // создание: формат, участники, группы — всё редактируемо
    Scheduled,  // бои сгенерированы, группы заблокированы, бои ещё не начались
    Active,     // бои идут
    Completed,
    Cancelled
}
