using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure;

namespace MarketTimeSeries.IntegrationTests.Repositories;

public sealed class InMemoryTimeSeriesRepositoryTests : TimeSeriesRepositoryContract
{
    protected override ValueTask<ITimeSeriesRepository> CreateEmptyRepositoryAsync() =>
        ValueTask.FromResult<ITimeSeriesRepository>(new InMemoryTimeSeriesRepository());
}
