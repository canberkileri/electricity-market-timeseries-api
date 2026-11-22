using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MarketTimeSeries.IntegrationTests;

/// <summary>
/// Runs the real API in memory in the "Testing" environment: no seed data and no OpenAPI endpoints,
/// so every test starts from an empty store. Tests that need Development ask for it explicitly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
