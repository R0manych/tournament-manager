namespace TournamentManager.Domain.Files;

public interface IFileStorage
{
    Task<Guid> SaveAsync(Guid tournamentId, string fileName, string contentType, Stream content, CancellationToken ct);
    Task<DocumentDownload?> GetAsync(Guid documentId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid documentId, CancellationToken ct);
}

public record DocumentDownload(string FileName, string ContentType, Stream Content);
