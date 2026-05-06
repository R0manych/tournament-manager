using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TournamentManager.Infrastructure.Persistence;

public class TournamentDbContextFactory : IDesignTimeDbContextFactory<TournamentDbContext>
{
    public TournamentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TournamentDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=tournament_manager;Username=postgres;Password=postgres")
            .Options;

        return new TournamentDbContext(options);
    }
}
