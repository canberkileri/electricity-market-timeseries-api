using DotNet.Testcontainers.Builders;
using MarketTimeSeries.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Testcontainers.MsSql;

namespace MarketTimeSeries.IntegrationTests.Repositories;

/// <summary>
/// A real SQL Server in a container, shared by the tests of one class. Without Docker the tests are skipped,
/// so a fresh clone still passes; in CI (where GitHub sets CI=true) a missing Docker fails the run instead,
/// so these tests can never be skipped silently there.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    /// <summary>Why the tests cannot run, or null when SQL Server is available.</summary>
    public string? SkipReason { get; private set; }

    internal IDbContextFactory<TimeSeriesDbContext> ContextFactory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException) when (Environment.GetEnvironmentVariable("CI") is null)
        {
            SkipReason = "Docker is not available, so the SQL Server tests are skipped. Start Docker to run them.";
            return;
        }

        var options = new DbContextOptionsBuilder<TimeSeriesDbContext>().UseSqlServer(_container.GetConnectionString()).Options;
        ContextFactory = new PooledDbContextFactory<TimeSeriesDbContext>(options);

        await using var context = await ContextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>Empties the tables, so every test starts from the same state.</summary>
    public async Task ClearAsync()
    {
        await using var context = await ContextFactory.CreateDbContextAsync();
        await context.Points.ExecuteDeleteAsync();
        await context.Series.ExecuteDeleteAsync();
        await context.Identities.ExecuteDeleteAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
