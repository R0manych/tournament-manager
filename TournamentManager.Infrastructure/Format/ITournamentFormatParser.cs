using TournamentManager.Domain.Format;

namespace TournamentManager.Infrastructure.Format;

public interface ITournamentFormatParser
{
    FormatParseResult Parse(string yaml);
}

public record FormatParseResult(
    bool IsValid,
    TournamentFormat? Format,
    IReadOnlyList<FormatError> Errors);

public record FormatError(string Path, string Code, string Message);
