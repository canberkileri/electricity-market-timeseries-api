using MarketTimeSeries.Application.Storage;
using MarketTimeSeries.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketTimeSeries.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers storage, chosen by "Storage:Provider": InMemory (the default, needs nothing installed) or
    /// SqlServer (needs "ConnectionStrings:TimeSeries"). Nothing outside this method knows which one is used.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"] ?? "InMemory";

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("TimeSeries")
                ?? throw new InvalidOperationException("Storage:Provider is SqlServer, but ConnectionStrings:TimeSeries is not set.");

            services.AddDbContextFactory<TimeSeriesDbContext>(options => options.UseSqlServer(connectionString));
            // Singleton: each call creates its own DbContext from the factory.
            services.AddSingleton<ITimeSeriesRepository, SqlServerTimeSeriesRepository>();
        }
        else if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            // Singleton: the in-memory store must live as long as the application.
            services.AddSingleton<ITimeSeriesRepository, InMemoryTimeSeriesRepository>();
        }
        else
        {
            throw new InvalidOperationException($"Unknown Storage:Provider '{provider}'. Use InMemory or SqlServer.");
        }

        return services;
    }

    /// <summary>
    /// Creates the SQL Server tables if they do not exist yet; does nothing for the in-memory store. Meant for local
    /// development: in production the schema would be deployed by the pipeline, not by the application.
    /// </summary>
    public static async Task EnsureStorageCreatedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IDbContextFactory<TimeSeriesDbContext>>() is not { } contextFactory)
        {
            return;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }
}
