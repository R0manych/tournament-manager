using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TournamentManager.Domain.Files;
using TournamentManager.Infrastructure.Files;
using TournamentManager.Infrastructure.Format;
using TournamentManager.Infrastructure.Persistence;

namespace TournamentManager.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTournamentManager(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TournamentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddSingleton<ITournamentFormatParser, TournamentFormatParser>();

        //services.AddScoped<IFileStorage, PostgresFileStorage>();

        return services;
    }
}
