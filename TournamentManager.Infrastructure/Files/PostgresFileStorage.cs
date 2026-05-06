using Microsoft.EntityFrameworkCore;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Files;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Infrastructure.Files;

/*
public class PostgresFileStorage(TournamentDbContext db) : IFileStorage
{
    public async Task<Guid> SaveAsync(Guid tournamentId, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = ms.Length,
            Content = ms.ToArray(),
            UploadedAt = DateTime.UtcNow
        };

        //db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        return document.Id;
    }

    public async Task<DocumentDownload?> GetAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
            return null;

        return new DocumentDownload(document.FileName, document.ContentType, new MemoryStream(document.Content));
    }

    public async Task<bool> DeleteAsync(Guid documentId, CancellationToken ct)
    {
        var deleted = await db.Documents
            .Where(d => d.Id == documentId)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}*/
