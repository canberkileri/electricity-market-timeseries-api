using System.Net;

namespace MarketTimeSeries.IntegrationTests.Endpoints;

public sealed class HealthEndpointTests(ApiFactory factory)
    : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetHealth_ApiIsRunning_Returns200Ok()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
