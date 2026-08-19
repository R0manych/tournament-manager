using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zettel.Domain.Files;
using Zettel.Infrastructure.Files;
using Zettel.Infrastructure.Format;
using Zettel.Infrastructure.Persistence;

namespace Zettel.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZettel(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<TournamentDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddSingleton<ITournamentFormatParser, TournamentFormatParser>();

        //services.AddScoped<IFileStorage, PostgresFileStorage>();

        return services;
    }
}
