using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class TimeSeriesUpdateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object Production(DateTime start, params decimal[] values) => new
    {
        type = "Production",
        biddingZone = "SE3",
        unit = "MW",
        resolution = "PT60M",
        start = Iso(start),
        values,
    };

    private async Task<Uri> CreateAsync(object request)
    {
        var response = await _client.PostAsJsonAsync(BasePath, request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return response.Headers.Location!;
    }

    private Task<HttpResponseMessage> PutAsync(Uri location, object request, string? ifMatch = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Put, location) { Content = JsonContent.Create(request) };
        if (ifMatch is not null)
        {
            message.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return _client.SendAsync(message, Ct);
    }

    [Fact]
    public async Task Put_MatchingIfMatch_Returns200WithNextVersionAndETag()
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m, 110m));

        var response = await PutAsync(location, Production(day, 120m, 130m, 140m), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new EntityTagHeaderValue("\"2\""), response.Headers.ETag);
        var body = await ReadJsonAsync(response);
        Assert.Equal(2, body.GetProperty("version").GetInt32());
        Assert.Equal(3, body.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task Put_StaleIfMatch_Returns412AndKeepsSeries()
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m));
        await PutAsync(location, Production(day, 200m), ifMatch: "\"1\"");

        var response = await PutAsync(location, Production(day, 300m), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var stored = await ReadJsonAsync(await _client.GetAsync(location, Ct));
        Assert.Equal(2, stored.GetProperty("version").GetInt32());
        Assert.Equal(200m, stored.GetProperty("points")[0].GetProperty("value").GetDecimal());
    }

    [Theory]
    [InlineData(null)] // no If-Match: last write wins, which the brief allows
    [InlineData("*")]  // any version
    public async Task Put_WithoutVersionCheck_Returns200(string? ifMatch)
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m));

        var response = await PutAsync(location, Production(day, 150m), ifMatch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_MalformedIfMatch_Returns400()
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m));

        var response = await PutAsync(location, Production(day, 150m), ifMatch: "version-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_PeriodOverlappingAnotherSeries_Returns409()
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m, 100m));
        await CreateAsync(Production(day.AddHours(12), 100m));

        var response = await PutAsync(location, Production(day.AddHours(11), 1m, 2m), ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var response = await PutAsync(new Uri($"{BasePath}/{Guid.NewGuid()}", UriKind.Relative), Production(UniqueDay(), 1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_BrokenDomainRules_Returns400WithErrorPerField()
    {
        var day = UniqueDay();
        var location = await CreateAsync(Production(day, 100m));

        var response = await PutAsync(location, Production(day.AddMinutes(30), -5m));

        await AssertValidationProblemAsync(response, "start", "values");
    }
}
