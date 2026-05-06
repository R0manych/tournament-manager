namespace TournamentManager.Domain.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public byte[] Content { get; set; } = null!;
    public DateTime UploadedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
