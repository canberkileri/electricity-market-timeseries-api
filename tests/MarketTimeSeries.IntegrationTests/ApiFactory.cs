using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MarketTimeSeries.IntegrationTests;

/// <summary>
/// Runs the real API in memory in the "Testing" environment: the in-memory store (asked for explicitly, since SQL Server
/// is the default), no seed data and no OpenAPI endpoints, so every test starts from an empty, fast store.
/// Tests that need Development or SQL Server ask for it explicitly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Testing").UseSetting("Storage:Provider", "InMemory");
}
