using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TournamentManager.Infrastructure.Persistence;

public class TournamentDbContextFactory : IDesignTimeDbContextFactory<TournamentDbContext>
{
    public TournamentDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Port=5432;Database=tournament_manager;Username=postgres;Password=postgres"
            //Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__Postgres environment variable before running migrations.");

        var options = new DbContextOptionsBuilder<TournamentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TournamentDbContext(options);
    }
}
