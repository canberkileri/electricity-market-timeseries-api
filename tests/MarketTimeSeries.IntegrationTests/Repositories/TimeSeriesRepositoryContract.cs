using MarketTimeSeries.Application.Storage;
using MarketTimeSeries.Domain;
namespace MarketTimeSeries.IntegrationTests.Repositories;

/// <summary>
/// The behaviour every ITimeSeriesRepository must have. Each storage runs these same tests through a subclass,
/// so the in-memory store and SQL Server are held to one contract.
/// </summary>
public abstract class TimeSeriesRepositoryContract : IAsyncLifetime
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    private ITimeSeriesRepository? _repository;

    /// <summary>An empty repository for each test.</summary>
    protected ITimeSeriesRepository Repository => _repository!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _repository = await CreateEmptyRepositoryAsync();

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected abstract ValueTask<ITimeSeriesRepository> CreateEmptyRepositoryAsync();

    /// <summary>An hourly series; production in SE3 by default, each test changes only what it is about.</summary>
    private static TimeSeries Series(
        TimeSeriesType type = TimeSeriesType.Production,
        BiddingZone biddingZone = BiddingZone.SE3,
        BiddingZone? toBiddingZone = null,
        int startHour = 0,
        int hours = 3) =>
        TimeSeries.Create(
            new TimeSeriesDefinition(
                type,
                biddingZone,
                toBiddingZone,
                MeasurementUnit.MW,
                Resolution.PT60M,
                _midnight.AddHours(startHour),
                new decimal[hours]),
            now: _midnight).Value;

    private static SearchCriteria Criteria(
        BiddingZone? biddingZone = null,
        TimeSeriesType? type = null,
        TimeInterval? window = null,
        int page = 1,
        int pageSize = 50) =>
        new(biddingZone, type, window, page, pageSize);

    [Fact]
    public async Task AddAsync_NewSeries_ReturnsDoneAndStoresIt()
    {
        var series = Series();

        var outcome = await Repository.AddAsync(series, Ct);

        Assert.Equal(WriteOutcome.Done, outcome);
        AssertStored(series, await Repository.GetAsync(series.Id, Ct));
    }

    [Fact]
    public async Task AddAsync_OverlappingSeriesWithSameIdentity_ReturnsConflict()
    {
        await Repository.AddAsync(Series(startHour: 0, hours: 3), Ct);
        var overlapping = Series(startHour: 2, hours: 3);

        var outcome = await Repository.AddAsync(overlapping, Ct);

        Assert.Equal(WriteOutcome.Conflict, outcome);
        Assert.Null(await Repository.GetAsync(overlapping.Id, Ct));
    }

    /// <summary>Twenty requests race to store the same hours; the lock lets exactly one of them win.</summary>
    [Fact]
    public async Task AddAsync_ParallelOverlappingSeries_StoresExactlyOne()
    {
        var candidates = Enumerable.Range(0, 20).Select(_ => Series()).ToList();

        var outcomes = await Task.WhenAll(candidates.Select(series => Task.Run(() => Repository.AddAsync(series, Ct), Ct)));

        Assert.Single(outcomes, outcome => outcome == WriteOutcome.Done);
        Assert.Equal(1, (await Repository.SearchAsync(Criteria(), Ct)).TotalCount);
    }

    [Fact]
    public async Task ReplaceAsync_ExpectedVersionMatches_ReturnsDoneAndStoresReplacement()
    {
        var stored = Series();
        await Repository.AddAsync(stored, Ct);
        var replacement = stored.Replace(new TimeSeriesDefinition(
            stored.Type, stored.BiddingZone, null, stored.Unit, stored.Resolution, stored.Start, [1m, 2m]), _midnight).Value;

        var outcome = await Repository.ReplaceAsync(replacement, expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.Done, outcome);
        Assert.Equal(2, (await Repository.GetAsync(stored.Id, Ct))!.Version);
    }

    [Fact]
    public async Task ReplaceAsync_StaleExpectedVersion_ReturnsVersionMismatchAndKeepsStored()
    {
        var stored = Series();
        await Repository.AddAsync(stored, Ct);
        var replacement = stored.Replace(new TimeSeriesDefinition(
            stored.Type, stored.BiddingZone, null, stored.Unit, stored.Resolution, stored.Start, [1m]), _midnight).Value;

        var outcome = await Repository.ReplaceAsync(replacement, expectedVersion: 7, Ct);

        Assert.Equal(WriteOutcome.VersionMismatch, outcome);
        AssertStored(stored, await Repository.GetAsync(stored.Id, Ct));
    }

    [Fact]
    public async Task ReplaceAsync_UnknownSeries_ReturnsNotFound()
    {
        var outcome = await Repository.ReplaceAsync(Series(), expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ReplaceAsync_MovedIntoAnotherSeriesPeriod_ReturnsConflict()
    {
        var morning = Series(startHour: 0, hours: 3);
        var noon = Series(startHour: 12, hours: 3);
        await Repository.AddAsync(morning, Ct);
        await Repository.AddAsync(noon, Ct);
        var movedToNoon = morning.Replace(new TimeSeriesDefinition(
            morning.Type, morning.BiddingZone, null, morning.Unit, morning.Resolution, _midnight.AddHours(13), [1m]), _midnight).Value;

        var outcome = await Repository.ReplaceAsync(movedToNoon, expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.Conflict, outcome);
    }

    [Fact]
    public async Task DeleteAsync_StoredSeries_ReturnsDoneAndRemovesIt()
    {
        var series = Series();
        await Repository.AddAsync(series, Ct);

        Assert.Equal(WriteOutcome.Done, await Repository.DeleteAsync(series.Id, expectedVersion: null, Ct));
        Assert.Null(await Repository.GetAsync(series.Id, Ct));
        Assert.Equal(WriteOutcome.NotFound, await Repository.DeleteAsync(series.Id, expectedVersion: null, Ct));
    }

    [Fact]
    public async Task DeleteAsync_StaleExpectedVersion_ReturnsVersionMismatchAndKeepsSeries()
    {
        var series = Series();
        await Repository.AddAsync(series, Ct);

        var outcome = await Repository.DeleteAsync(series.Id, expectedVersion: 2, Ct);

        Assert.Equal(WriteOutcome.VersionMismatch, outcome);
        AssertStored(series, await Repository.GetAsync(series.Id, Ct));
    }

    [Fact]
    public async Task SearchAsync_ByBiddingZone_ReturnsFlowsAtEitherEnd()
    {
        var se3Production = Series(biddingZone: BiddingZone.SE3);
        var dk1ToSe3 = Series(TimeSeriesType.CrossBorderFlow, BiddingZone.DK1, BiddingZone.SE3);
        var se3ToFi = Series(TimeSeriesType.CrossBorderFlow, BiddingZone.SE3, BiddingZone.FI);
        var no1Production = Series(biddingZone: BiddingZone.NO1);
        foreach (var series in new[] { se3Production, dk1ToSe3, se3ToFi, no1Production })
        {
            await Repository.AddAsync(series, Ct);
        }

        var page = await Repository.SearchAsync(Criteria(biddingZone: BiddingZone.SE3), Ct);

        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, item => item.Id == no1Production.Id);
    }

    [Fact]
    public async Task SearchAsync_ByWindow_ReturnsOnlySeriesOverlappingIt()
    {
        var morning = Series(startHour: 0, hours: 3);
        var noon = Series(startHour: 12, hours: 3);
        await Repository.AddAsync(morning, Ct);
        await Repository.AddAsync(noon, Ct);

        var page = await Repository.SearchAsync(Criteria(window: new TimeInterval(_midnight.AddHours(3), _midnight.AddHours(13))), Ct);

        Assert.Equal([noon.Id], page.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData(3)]            // just past the last page
    [InlineData(int.MaxValue)] // (page - 1) * pageSize would overflow an int
    public async Task SearchAsync_PageBeyondLastPage_ReturnsNoItemsButTotalCount(int page)
    {
        await Repository.AddAsync(Series(), Ct);

        var result = await Repository.SearchAsync(Criteria(page: page, pageSize: 200), Ct);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_SecondPage_ReturnsRemainingItemsAndTotalCount()
    {
        for (var day = 0; day < 5; day++)
        {
            await Repository.AddAsync(Series(startHour: day * 24, hours: 24), Ct);
        }

        var page = await Repository.SearchAsync(Criteria(page: 2, pageSize: 3), Ct);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal([_midnight.AddDays(3), _midnight.AddDays(4)], page.Items.Select(item => item.Start));
    }

    [Fact]
    public async Task SearchAsync_WithWindow_ReturnsOnlyPointsInsideIt()
    {
        var series = TimeSeries.Create(
            new TimeSeriesDefinition(
                TimeSeriesType.Production, BiddingZone.SE3, null, MeasurementUnit.MW, Resolution.PT60M, _midnight, [0m, 1m, 2m, 3m, 4m]),
            now: _midnight).Value;
        await Repository.AddAsync(series, Ct);

        var page = await Repository.SearchAsync(Criteria(window: new TimeInterval(_midnight.AddHours(1), _midnight.AddHours(3))), Ct);

        var item = Assert.Single(page.Items);
        Assert.Equal(
            [new Point(_midnight.AddHours(1), 1m), new Point(_midnight.AddHours(2), 2m)],
            item.Points!);
        Assert.Equal(_midnight.AddHours(5), item.End);
    }

    [Fact]
    public async Task SearchAsync_WithoutWindow_ReturnsSeriesWithoutPoints()
    {
        await Repository.AddAsync(Series(), Ct);

        var page = await Repository.SearchAsync(Criteria(), Ct);

        Assert.Null(Assert.Single(page.Items).Points);
    }

    /// <summary>Stores may hand back a new object; what matters is that it holds the same series.</summary>
    private static void AssertStored(TimeSeries expected, TimeSeries? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.BiddingZone, actual.BiddingZone);
        Assert.Equal(expected.ToBiddingZone, actual.ToBiddingZone);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.Values, actual.Values);
    }
}
