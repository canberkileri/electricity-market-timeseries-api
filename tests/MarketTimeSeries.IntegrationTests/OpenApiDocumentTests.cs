using System.Net;
using Microsoft.AspNetCore.Hosting;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class OpenApiDocumentTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    /// <summary>
    /// The document is only served in Development, so this test asks for that environment explicitly.
    /// </summary>
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development"))
        .CreateClient();

    [Fact]
    public async Task GetOpenApiDocument_Development_DescribesEveryOperationWithEnumNames()
    {
        var response = await _client.GetAsync("/openapi/v1.json", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await ReadJsonAsync(response);
        var paths = document.GetProperty("paths");
        Assert.Equal(["get", "post"], paths.GetProperty(BasePath).EnumerateObject().Select(op => op.Name).Order());
        Assert.Equal(["delete", "get", "put"], paths.GetProperty($"{BasePath}/{{id}}").EnumerateObject().Select(op => op.Name).Order());
        var zones = document.GetProperty("components").GetProperty("schemas").GetProperty("BiddingZone").GetProperty("enum");
        Assert.Contains(zones.EnumerateArray(), zone => zone.GetString() == "SE3");
    }
}
