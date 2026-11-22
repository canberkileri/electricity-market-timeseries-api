using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class TimeSeriesDeleteTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object Consumption(DateTime start) => new
    {
        type = "Consumption",
        biddingZone = "DK1",
        unit = "MW",
        resolution = "PT15M",
        start = Iso(start),
        values = new[] { 3100m, 3080m, 3050m, 3020m },
    };

    private async Task<HttpResponseMessage> CreateAsync(DateTime start) =>
        await _client.PostAsJsonAsync(BasePath, Consumption(start), Ct);

    [Fact]
    public async Task Delete_StoredSeries_Returns204AndGetReturns404()
    {
        var location = (await CreateAsync(UniqueDay())).Headers.Location!;

        var response = await _client.DeleteAsync(location, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(location, Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.DeleteAsync($"{BasePath}/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Delete_StoredSeries_FreesItsPeriodForANewSeries()
    {
        var day = UniqueDay();
        var location = (await CreateAsync(day)).Headers.Location!;

        await _client.DeleteAsync(location, Ct);
        var recreated = await CreateAsync(day);

        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);
    }
}
