using MarketTimeSeries.Application.Storage;
using MarketTimeSeries.Infrastructure.InMemory;
using MarketTimeSeries.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketTimeSeries.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers storage, chosen by "Storage:Provider": SqlServer (the default, needs "ConnectionStrings:TimeSeries")
    /// or InMemory (needs nothing installed, data lost on restart). In memory must be asked for explicitly, so a server
    /// with missing configuration fails at startup instead of silently keeping data in memory.
    /// Nothing outside this method knows which store is used.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"] ?? "SqlServer";

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
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (SqlException exception)
        {
            throw new StorageUnavailableException(
                $"SQL Server at {context.Database.GetDbConnection().DataSource} is not available. Start it with "
                + "\"docker compose up -d\", or run without it: dotnet run --project src/MarketTimeSeries.Api --launch-profile inmemory",
                exception);
        }
    }
}
