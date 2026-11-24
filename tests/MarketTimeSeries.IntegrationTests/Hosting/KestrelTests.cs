using System.Net;
using System.Text;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests.Hosting;

/// <summary>
/// Runs on real Kestrel instead of the in-memory test server, because only Kestrel enforces the body size limit and
/// writes the Server header.
/// </summary>
public sealed class KestrelTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public KestrelTests() => _factory.UseKestrel(0); // any free port

    public void Dispose() => _factory.Dispose();

    /// <summary>A body over 1 MB is a client mistake: 413 Problem Details, not a 500.</summary>
    [Fact]
    public async Task Post_BodyOverOneMegabyte_Returns413ProblemDetails()
    {
        using var client = _factory.CreateClient();
        var body = "{\"values\":[" + string.Join(',', Enumerable.Repeat("1", 600_000)) + "]}";

        using var response = await client.PostAsync(BasePath, new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options")); // error responses keep it
    }

    /// <summary>Responses neither name the server nor let browsers guess their content type.</summary>
    [Fact]
    public async Task GetHealth_RealServer_SendsNoServerHeaderAndNoSniff()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health", Ct);

        Assert.Empty(response.Headers.Server);
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
    }
}
