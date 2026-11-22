using System.Net;
using Microsoft.AspNetCore.Hosting;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class SeedDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Seed data is only added in Development, so this test asks for that environment explicitly.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development"))
        .CreateClient();

    [Fact]
    public async Task Search_TodayInDevelopment_ReturnsTheThreeSeedSeries()
    {
        var today = DateTime.UtcNow.Date;

        var response = await _client.GetAsync($"{BasePath}?from={Iso(today)}&to={Iso(today.AddDays(1))}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            ["Consumption DK1", "CrossBorderFlow DK1", "Production SE3"],
            items.Select(item => $"{item.GetProperty("type").GetString()} {item.GetProperty("biddingZone").GetString()}").Order());
        Assert.Contains(
            items.Single(item => item.GetProperty("type").GetString() == "CrossBorderFlow").GetProperty("points").EnumerateArray(),
            point => point.GetProperty("value").GetDecimal() < 0);
    }
}
