using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TournamentManager.Infrastructure.Persistence;

public class TournamentDbContextFactory : IDesignTimeDbContextFactory<TournamentDbContext>
{
    public TournamentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__Postgres environment variable before running migrations.");

        var options = new DbContextOptionsBuilder<TournamentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TournamentDbContext(options);
    }
}
