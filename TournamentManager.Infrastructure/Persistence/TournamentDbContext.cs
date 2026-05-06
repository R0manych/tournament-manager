using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TournamentManager.Domain.Entities;
using TournamentManager.Domain.Format;

namespace TournamentManager.Infrastructure.Persistence;

public class TournamentDbContext(DbContextOptions<TournamentDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<TournamentParticipant> TournamentParticipants => Set<TournamentParticipant>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Exchange> Exchanges => Set<Exchange>();
    //public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // Tournament
        m.Entity<Tournament>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.Description).HasMaxLength(4000);
            b.Property(t => t.Nomination).HasMaxLength(100);
            b.Property(t => t.Location).HasMaxLength(200);
            b.Property(t => t.Status).HasConversion<string>();
            b.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");

            b.Property(t => t.Format)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                    v => string.IsNullOrEmpty(v) ? null : JsonSerializer.Deserialize<TournamentFormat>(v, JsonOptions));

            b.Property(t => t.FormatYaml).HasColumnType("text");

            b.HasMany(t => t.Participants)
                .WithOne(p => p.Tournament)
                .HasForeignKey(p => p.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(t => t.Matches)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            //b.HasMany(t => t.Documents)
            //    .WithOne(d => d.Tournament)
            //    .HasForeignKey(d => d.TournamentId)
            //    .OnDelete(DeleteBehavior.Cascade);
        });

        // Fighter
        m.Entity<Fighter>(b =>
        {
            b.HasKey(f => f.Id);
            b.Property(f => f.FirstName).IsRequired().HasMaxLength(100);
            b.Property(f => f.LastName).IsRequired().HasMaxLength(100);
            b.Property(f => f.Club).HasMaxLength(200);
            b.Property(f => f.CreatedAt).HasColumnType("timestamp with time zone");
        });

        // TournamentParticipant
        m.Entity<TournamentParticipant>(b =>
        {
            b.HasKey(p => new { p.TournamentId, p.FighterId });
            b.Property(p => p.RegisteredAt).HasColumnType("timestamp with time zone");

            b.HasOne(p => p.Fighter)
                .WithMany()
                .HasForeignKey(p => p.FighterId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Match
        m.Entity<Match>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>();
            b.Property(x => x.ScheduledAt).HasColumnType("timestamp with time zone");
            b.Property(x => x.StartedAt).HasColumnType("timestamp with time zone");
            b.Property(x => x.CurrentRoundStartedAt).HasColumnType("timestamp with time zone");
            b.Property(x => x.EndedAt).HasColumnType("timestamp with time zone");
            b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");

            b.HasOne(x => x.Fighter1)
                .WithMany()
                .HasForeignKey(x => x.Fighter1Id)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Fighter2)
                .WithMany()
                .HasForeignKey(x => x.Fighter2Id)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Exchanges)
                .WithOne(e => e.Match)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.TournamentId, x.Status });
            b.HasIndex(x => x.Fighter1Id);
            b.HasIndex(x => x.Fighter2Id);

            b.ToTable(t => t.HasCheckConstraint(
                "CK_Match_Fighter1NotEqualFighter2",
                "\"Fighter1Id\" <> \"Fighter2Id\""));
        });

        // Exchange
        m.Entity<Exchange>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Note).HasMaxLength(500);
            b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");

            b.HasIndex(e => new { e.MatchId, e.Sequence }).IsUnique();
            b.HasIndex(e => new { e.MatchId, e.RoundNumber });
        });

        // Document
        //m.Entity<Document>(b =>
        //{
        //    b.HasKey(d => d.Id);
        //    b.Property(d => d.FileName).IsRequired().HasMaxLength(255);
        //    b.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        //    b.Property(d => d.UploadedAt).HasColumnType("timestamp with time zone");

        //    b.HasIndex(d => d.TournamentId);
        //});
    }
}
