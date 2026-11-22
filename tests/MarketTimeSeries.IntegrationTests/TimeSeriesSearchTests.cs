using System.Net;
using System.Net.Http.Json;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class TimeSeriesSearchTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task CreateAsync(string type, string zone, string? toZone, DateTime start, int hours)
    {
        var request = new
        {
            type,
            biddingZone = zone,
            toBiddingZone = toZone,
            unit = "MW",
            resolution = "PT60M",
            start = Iso(start),
            values = Enumerable.Range(0, hours).Select(hour => (decimal)hour).ToArray(),
        };

        var response = await _client.PostAsJsonAsync(BasePath, request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task<HttpResponseMessage> SearchAsync(string query) => _client.GetAsync($"{BasePath}?{query}", Ct);

    [Fact]
    public async Task Search_ByBiddingZone_ReturnsFlowsAtEitherEnd()
    {
        var day = UniqueDay();
        await CreateAsync("Production", "SE3", null, day, 24);
        await CreateAsync("CrossBorderFlow", "DK1", "SE3", day, 24);
        await CreateAsync("CrossBorderFlow", "SE3", "FI", day, 24);
        await CreateAsync("Production", "NO1", null, day, 24);

        var response = await SearchAsync($"biddingZone=SE3&from={Iso(day)}&to={Iso(day.AddDays(1))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(
            body.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("biddingZone").GetString() == "NO1");
    }

    [Fact]
    public async Task Search_ByWindow_ClipsPointsToWindow()
    {
        var day = UniqueDay();
        await CreateAsync("Consumption", "FI", null, day, 6);

        var response = await SearchAsync($"biddingZone=FI&from={Iso(day.AddHours(2))}&to={Iso(day.AddHours(4))}");

        var item = Assert.Single((await ReadJsonAsync(response)).GetProperty("items").EnumerateArray());
        Assert.Equal(
            [Iso(day.AddHours(2)), Iso(day.AddHours(3))],
            item.GetProperty("points").EnumerateArray().Select(point => point.GetProperty("timestamp").GetString()));
    }

    [Fact]
    public async Task Search_SecondPage_ReturnsRemainingItemsAndTotalCount()
    {
        var day = UniqueDay();
        for (var offset = 0; offset < 3; offset++)
        {
            await CreateAsync("Production", "SE1", null, day.AddDays(offset), 24);
        }

        var response = await SearchAsync($"biddingZone=SE1&from={Iso(day)}&to={Iso(day.AddDays(3))}&page=2&pageSize=2");

        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(2, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData(0)]  // from equal to to
    [InlineData(-1)] // from after to
    public async Task Search_FromNotBeforeTo_Returns400(int toOffsetHours)
    {
        var from = UniqueDay();

        var response = await SearchAsync($"from={Iso(from)}&to={Iso(from.AddHours(toOffsetHours))}");

        await AssertValidationProblemAsync(response, "to");
    }

    [Fact]
    public async Task Search_WindowLongerThan31Days_Returns400()
    {
        var from = UniqueDay();

        var response = await SearchAsync($"from={Iso(from)}&to={Iso(from.AddDays(31).AddHours(1))}");

        await AssertValidationProblemAsync(response, "to");
    }

    [Fact]
    public async Task Search_OnlyFrom_Returns400()
    {
        var response = await SearchAsync($"from={Iso(UniqueDay())}");

        await AssertValidationProblemAsync(response, "to");
    }

    [Fact]
    public async Task Search_TimestampWithoutOffset_Returns400()
    {
        // Rejected on every server, instead of being read as that server's local time.
        var response = await SearchAsync("from=2030-01-01T00:00:00&to=2030-01-02T00:00:00Z");

        await AssertValidationProblemAsync(response, "from");
    }

    [Fact]
    public async Task Search_PageSizeAboveMaximum_Returns400()
    {
        var response = await SearchAsync("pageSize=201");

        await AssertValidationProblemAsync(response, "pageSize");
    }
}
