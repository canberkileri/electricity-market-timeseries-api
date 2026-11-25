using MarketTimeSeries.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests.Hosting;

public sealed class StorageStartupTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => KeyValuePair.Create(setting.Key, (string?)setting.Value)))
            .Build();

    /// <summary>
    /// SQL Server is the default, so a server with missing configuration fails at startup instead of silently keeping
    /// data in memory.
    /// </summary>
    [Fact]
    public void AddInfrastructure_NoProviderConfigured_RequiresSqlServerConnectionString()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(Configuration()));

        Assert.Contains("ConnectionStrings:TimeSeries", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Without a running SQL Server, startup says how to fix it instead of showing a raw SqlException.</summary>
    [Fact]
    public async Task EnsureStorageCreatedAsync_SqlServerUnreachable_ThrowsStorageUnavailableWithBothFixes()
    {
        var configuration = Configuration(
            ("Storage:Provider", "SqlServer"),
            ("ConnectionStrings:TimeSeries", "Server=localhost,1;Database=Unused;User Id=sa;Password=unused;TrustServerCertificate=True;Connect Timeout=2"));
        await using var services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<StorageUnavailableException>(() => services.EnsureStorageCreatedAsync(Ct));

        Assert.Contains("docker compose up -d", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--launch-profile inmemory", exception.Message, StringComparison.Ordinal);
    }
}
