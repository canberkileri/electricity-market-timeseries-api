using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MarketTimeSeries.IntegrationTests;

public sealed class TimeSeriesCreateAndGetTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BasePath = "/api/v1/time-series";

    // The tests share one running API; giving each test its own day keeps their series from overlapping.
    private static int _nextDay;

    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UniqueDay() =>
        new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Interlocked.Increment(ref _nextDay)).ToString("yyyy-MM-dd");

    private static object Flow(string day, string startTime = "00:00:00") => new
    {
        type = "CrossBorderFlow",
        biddingZone = "DK1",
        toBiddingZone = "SE3",
        unit = "MW",
        resolution = "PT15M",
        start = $"{day}T{startTime}Z",
        values = new[] { 420.5m, 415.0m, -12.5m, 401.7m },
    };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct));

    [Fact]
    public async Task Post_ValidSeries_Returns201WithLocationAndETag()
    {
        var response = await _client.PostAsJsonAsync(BasePath, Flow(UniqueDay()), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.EndsWith($"{BasePath}/{body.GetProperty("id").GetString()}", response.Headers.Location!.ToString());
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
        Assert.Equal(1, body.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Get_CreatedSeries_ReturnsSameDataWithPoints()
    {
        var day = UniqueDay();
        var created = await _client.PostAsJsonAsync(BasePath, Flow(day), Ct);

        var response = await _client.GetAsync(created.Headers.Location, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
        var body = await ReadJsonAsync(response);
        Assert.Equal("CrossBorderFlow", body.GetProperty("type").GetString());
        Assert.Equal("DK1", body.GetProperty("biddingZone").GetString());
        Assert.Equal("SE3", body.GetProperty("toBiddingZone").GetString());
        Assert.Equal($"{day}T00:00:00Z", body.GetProperty("start").GetString());
        Assert.Equal($"{day}T01:00:00Z", body.GetProperty("end").GetString());
        var points = body.GetProperty("points").EnumerateArray().ToList();
        Assert.Equal(4, points.Count);
        Assert.Equal($"{day}T00:15:00Z", points[1].GetProperty("timestamp").GetString());
        Assert.Equal(-12.5m, points[2].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Post_BrokenDomainRules_Returns400WithErrorPerField()
    {
        var request = new
        {
            type = "CrossBorderFlow",
            biddingZone = "DK1",
            unit = "MW",
            resolution = "PT15M",
            start = $"{UniqueDay()}T00:07:00Z",
            values = new[] { 1m },
        };

        var response = await _client.PostAsJsonAsync(BasePath, request, Ct);

        await AssertValidationProblemAsync(response, "start", "toBiddingZone");
    }

    [Fact]
    public async Task Post_UnknownBiddingZone_Returns400WithBiddingZoneError()
    {
        var request = new
        {
            type = "Production",
            biddingZone = "SE9",
            unit = "MW",
            resolution = "PT60M",
            start = $"{UniqueDay()}T00:00:00Z",
            values = new[] { 1m },
        };

        var response = await _client.PostAsJsonAsync(BasePath, request, Ct);

        await AssertValidationProblemAsync(response, "biddingZone");
    }

    [Fact]
    public async Task Post_MissingRequiredFields_Returns400WithErrorPerField()
    {
        var request = new { type = "Production", biddingZone = "SE3", values = new[] { 1m } };

        var response = await _client.PostAsJsonAsync(BasePath, request, Ct);

        await AssertValidationProblemAsync(response, "unit", "resolution", "start");
    }

    [Fact]
    public async Task Post_OverlappingSeries_Returns409ProblemDetails()
    {
        var day = UniqueDay();
        await _client.PostAsJsonAsync(BasePath, Flow(day), Ct);

        var response = await _client.PostAsJsonAsync(BasePath, Flow(day, startTime: "00:30:00"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Overlapping time series", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync($"{BasePath}/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.Equal(
            expectedFields.Order(StringComparer.Ordinal),
            errors.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
    }
}
