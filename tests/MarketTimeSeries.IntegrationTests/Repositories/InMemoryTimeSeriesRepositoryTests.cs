using MarketTimeSeries.Application.Storage;
using MarketTimeSeries.Infrastructure.InMemory;

namespace MarketTimeSeries.IntegrationTests.Repositories;

public sealed class InMemoryTimeSeriesRepositoryTests : TimeSeriesRepositoryContract
{
    protected override ValueTask<ITimeSeriesRepository> CreateEmptyRepositoryAsync() =>
        ValueTask.FromResult<ITimeSeriesRepository>(new InMemoryTimeSeriesRepository());
}
