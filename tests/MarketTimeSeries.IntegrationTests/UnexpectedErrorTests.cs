using System.Net;
using MarketTimeSeries.Application;
using MarketTimeSeries.Domain;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static MarketTimeSeries.IntegrationTests.ApiTestHelpers;

namespace MarketTimeSeries.IntegrationTests;

public sealed class UnexpectedErrorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SecretDetail = "Connection string Server=prod-db-01;Password=hunter2";

    // The real API, with storage replaced by one that fails the way a broken database would.
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ITimeSeriesRepository, FailingRepository>()))
        .CreateClient();

    [Fact]
    public async Task Get_StorageThrows_Returns500ProblemDetailsWithoutInternalDetails()
    {
        var response = await _client.GetAsync($"{BasePath}/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"status\":500", body, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(FailingRepository), body, StringComparison.Ordinal); // no stack trace
    }

    private sealed class FailingRepository : ITimeSeriesRepository
    {
        public Task<TimeSeries?> GetAsync(Guid id, CancellationToken cancellationToken) => throw Failure();

        public Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken) => throw Failure();

        public Task<WriteOutcome> AddAsync(TimeSeries series, CancellationToken cancellationToken) => throw Failure();

        public Task<WriteOutcome> ReplaceAsync(TimeSeries replacement, int expectedVersion, CancellationToken cancellationToken) =>
            throw Failure();

        public Task<WriteOutcome> DeleteAsync(Guid id, int? expectedVersion, CancellationToken cancellationToken) => throw Failure();

        private static InvalidOperationException Failure() => new(SecretDetail);
    }
}
