using MarketTimeSeries.Application.Storage;
using MarketTimeSeries.Infrastructure.SqlServer;

namespace MarketTimeSeries.IntegrationTests.Repositories;

[Collection(SqlServerFixture.Collection)]
public sealed class SqlServerTimeSeriesRepositoryTests(SqlServerFixture sqlServer) : TimeSeriesRepositoryContract
{
    protected override async ValueTask<ITimeSeriesRepository> CreateEmptyRepositoryAsync()
    {
        if (sqlServer.SkipReason is { } reason)
        {
            Assert.Skip(reason);
        }

        await sqlServer.ClearAsync();
        return new SqlServerTimeSeriesRepository(sqlServer.ContextFactory);
    }
}
