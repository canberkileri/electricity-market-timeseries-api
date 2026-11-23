using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure.SqlServer;

namespace MarketTimeSeries.IntegrationTests.Repositories;

public sealed class SqlServerTimeSeriesRepositoryTests(SqlServerFixture sqlServer)
    : TimeSeriesRepositoryContract, IClassFixture<SqlServerFixture>
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
