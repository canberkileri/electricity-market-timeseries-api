using System.Net;
using System.Net.Http.Json;
using MarketTimeSeries.IntegrationTests.Repositories;
using Microsoft.AspNetCore.Hosting;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests.Hosting;

/// <summary>
/// The whole API through HTTP with Storage:Provider=SqlServer: proves the provider switch and the wiring,
/// not only the repository. Shares the SQL Server container with the repository tests.
/// </summary>
[Collection(SqlServerFixture.Collection)]
public sealed class SqlServerApiTests(SqlServerFixture sqlServer, ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client()
    {
        if (sqlServer.SkipReason is { } reason)
        {
            Assert.Skip(reason);
        }

        return factory
            .WithWebHostBuilder(builder => builder
                .UseSetting("Storage:Provider", "SqlServer")
                .UseSetting("ConnectionStrings:TimeSeries", sqlServer.ConnectionString))
            .CreateClient();
    }

    private static object Flow(DateTime start, params decimal[] values) => new
    {
        type = "CrossBorderFlow",
        biddingZone = "NO2",
        toBiddingZone = "DK1",
        unit = "MW",
        resolution = "PT60M",
        start = Iso(start),
        values,
    };

    [Fact]
    public async Task PostThenGet_SqlServerProvider_StoresAndReturnsTheSeries()
    {
        var client = Client();
        var day = UniqueDay();

        var created = await client.PostAsJsonAsync(BasePath, Flow(day, 1400m, -250.5m, 980m), Ct);
        var fetched = await client.GetAsync(created.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var points = (await ReadJsonAsync(fetched)).GetProperty("points").EnumerateArray().ToList();
        Assert.Equal([1400m, -250.5m, 980m], points.Select(point => point.GetProperty("value").GetDecimal()));
    }

    [Fact]
    public async Task Post_OverlappingSeriesOnSqlServer_Returns409()
    {
        var client = Client();
        var day = UniqueDay();
        await client.PostAsJsonAsync(BasePath, Flow(day, 1m, 2m, 3m), Ct);

        var response = await client.PostAsJsonAsync(BasePath, Flow(day.AddHours(2), 4m), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Search_WindowOnSqlServer_ReturnsOnlyPointsInsideIt()
    {
        var client = Client();
        var day = UniqueDay();
        await client.PostAsJsonAsync(BasePath, Flow(day, 0m, 1m, 2m, 3m, 4m), Ct);

        var response = await client.GetAsync($"{BasePath}?biddingZone=NO2&from={Iso(day.AddHours(1))}&to={Iso(day.AddHours(3))}", Ct);

        var item = Assert.Single((await ReadJsonAsync(response)).GetProperty("items").EnumerateArray());
        Assert.Equal([1m, 2m], item.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("value").GetDecimal()));
    }
}
