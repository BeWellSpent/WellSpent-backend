using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace WellSpent.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires <see cref="WellSpentDbContext"/> against the existing Postgres
    /// schema. Mirrors the Go backend's internal/db/conn.go: a bounded pool
    /// (MinPoolSize=0 for Neon scale-to-zero friendliness, MaxPoolSize=10) and
    /// a distinct application_name per deployed process, so connections stay
    /// identifiable in pg_stat_activity.
    /// </summary>
    public static IServiceCollection AddWellSpentDbContext(
        this IServiceCollection services, string databaseUrl, string applicationName)
    {
        var csBuilder = PostgresConnectionString.FromDatabaseUrl(databaseUrl);
        csBuilder.ApplicationName = applicationName;
        csBuilder.MinPoolSize = 0;
        csBuilder.MaxPoolSize = 10;

        services.AddDbContext<WellSpentDbContext>(options =>
            options.UseNpgsql(csBuilder.ConnectionString));

        return services;
    }
}
