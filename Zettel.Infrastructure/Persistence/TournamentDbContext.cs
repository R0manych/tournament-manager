using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Zettel.Domain.Entities;
using Zettel.Domain.Format;

namespace Zettel.Infrastructure.Persistence;

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
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<TournamentGroup> TournamentGroups => Set<TournamentGroup>();
    public DbSet<MatchPlacement> MatchPlacements => Set<MatchPlacement>();
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
            b.Property(t => t.ParticipantKind)
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(Domain.Enums.ParticipantKind.Fighter);
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

            b.HasMany(t => t.Teams)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(t => t.Encounters)
                .WithOne(x => x.Tournament)
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(t => t.Groups)
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

        // TournamentParticipant — polymorphic; ParticipantId references Fighter or Team
        // depending on Tournament.ParticipantKind. No FK enforced at DB level.
        m.Entity<TournamentParticipant>(b =>
        {
            b.HasKey(p => new { p.TournamentId, p.ParticipantId });
            b.Property(p => p.Seed);
            b.Property(p => p.RegisteredAt).HasColumnType("timestamp with time zone");
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
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Fighter2)
                .WithMany()
                .HasForeignKey(x => x.Fighter2Id)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Team1)
                .WithMany()
                .HasForeignKey(x => x.Team1Id)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Team2)
                .WithMany()
                .HasForeignKey(x => x.Team2Id)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Exchanges)
                .WithOne(e => e.Match)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Encounter)
                .WithMany(e => e.Bouts)
                .HasForeignKey(x => x.EncounterId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.TournamentId, x.Status });
            b.HasIndex(x => x.Fighter1Id);
            b.HasIndex(x => x.Fighter2Id);
            b.HasIndex(x => new { x.EncounterId, x.BoutNumber })
                .IsUnique()
                .HasFilter("\"EncounterId\" IS NOT NULL");

            b.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_Match_Fighter1NotEqualFighter2",
                    "\"Fighter2Id\" IS NULL OR \"Fighter1Id\" <> \"Fighter2Id\"");
                t.HasCheckConstraint(
                    "CK_Match_Team1NotEqualTeam2",
                    "\"Team2Id\" IS NULL OR \"Team1Id\" <> \"Team2Id\"");
                t.HasCheckConstraint(
                    "CK_Match_FighterXorTeam",
                    "(\"Fighter1Id\" IS NOT NULL AND \"Team1Id\" IS NULL AND \"Team2Id\" IS NULL)" +
                    " OR (\"Team1Id\" IS NOT NULL AND \"Fighter1Id\" IS NULL AND \"Fighter2Id\" IS NULL)");
            });
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

        // Team
        m.Entity<Team>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.Club).HasMaxLength(200);
            b.Property(t => t.City).HasMaxLength(100);
            b.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");

            b.HasIndex(t => new { t.TournamentId, t.Name }).IsUnique();
        });

        // TeamMember
        m.Entity<TeamMember>(b =>
        {
            b.HasKey(tm => new { tm.TeamId, tm.FighterId });
            b.Property(tm => tm.AddedAt).HasColumnType("timestamp with time zone");

            b.HasOne(tm => tm.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(tm => tm.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(tm => tm.Fighter)
                .WithMany()
                .HasForeignKey(tm => tm.FighterId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(tm => new { tm.TeamId, tm.Position }).IsUnique();
        });

        // Encounter
        m.Entity<Encounter>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Status).HasConversion<string>();
            b.Property(e => e.ScheduledAt).HasColumnType("timestamp with time zone");
            b.Property(e => e.StartedAt).HasColumnType("timestamp with time zone");
            b.Property(e => e.EndedAt).HasColumnType("timestamp with time zone");
            b.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");

            b.HasOne(e => e.Participant1)
                .WithMany()
                .HasForeignKey(e => e.Participant1Id)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(e => e.Participant2)
                .WithMany()
                .HasForeignKey(e => e.Participant2Id)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(e => new { e.TournamentId, e.Status });

            b.ToTable(t => t.HasCheckConstraint(
                "CK_Encounter_Participant1NotEqualParticipant2",
                "\"Participant1Id\" <> \"Participant2Id\""));
        });

        // TournamentGroup — ParticipantIds maps to uuid[] (ordered, polymorphic ids)
        m.Entity<TournamentGroup>(b =>
        {
            b.HasKey(g => g.Id);
            b.Property(g => g.PhaseId).IsRequired().HasMaxLength(100);
            b.Property(g => g.Label).IsRequired().HasMaxLength(10);
            b.Property(g => g.UpdatedAt).HasColumnType("timestamp with time zone");

            b.HasIndex(g => new { g.TournamentId, g.PhaseId, g.Label }).IsUnique();
        });

        // MatchPlacement — one bracket cell holds at most one match, and a match sits in at
        // most one cell (docs/08, invariant 43). Both are enforced by unique indexes rather
        // than by application code, because the playoff generator relies on the 409 they
        // produce for its idempotency.
        m.Entity<MatchPlacement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.PhaseId).IsRequired().HasMaxLength(100);
            b.Property(x => x.RoundId).IsRequired().HasMaxLength(100);
            b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");

            b.HasOne(x => x.Tournament)
                .WithMany()
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting the match frees its cell.
            b.HasOne(x => x.Match)
                .WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.TournamentId, x.PhaseId, x.RoundId, x.SlotIndex }).IsUnique();
            b.HasIndex(x => x.MatchId).IsUnique();
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
