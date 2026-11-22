using MarketTimeSeries.Application;
using MarketTimeSeries.Domain;
using MarketTimeSeries.Infrastructure;

namespace MarketTimeSeries.UnitTests.Infrastructure;

public sealed class InMemoryTimeSeriesRepositoryTests
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTimeSeriesRepository _repository = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // An hourly series; production in SE3 by default, each test changes only what it is about.
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

        var outcome = await _repository.AddAsync(series, Ct);

        Assert.Equal(WriteOutcome.Done, outcome);
        Assert.Same(series, await _repository.GetAsync(series.Id, Ct));
    }

    [Fact]
    public async Task AddAsync_OverlappingSeriesWithSameIdentity_ReturnsConflict()
    {
        await _repository.AddAsync(Series(startHour: 0, hours: 3), Ct);
        var overlapping = Series(startHour: 2, hours: 3);

        var outcome = await _repository.AddAsync(overlapping, Ct);

        Assert.Equal(WriteOutcome.Conflict, outcome);
        Assert.Null(await _repository.GetAsync(overlapping.Id, Ct));
    }

    [Fact]
    public async Task AddAsync_ParallelOverlappingSeries_StoresExactlyOne()
    {
        // Twenty requests race to store the same hours; the lock lets exactly one of them win.
        var candidates = Enumerable.Range(0, 20).Select(_ => Series()).ToList();

        var outcomes = await Task.WhenAll(candidates.Select(series => Task.Run(() => _repository.AddAsync(series, Ct), Ct)));

        Assert.Single(outcomes, outcome => outcome == WriteOutcome.Done);
        Assert.Equal(1, (await _repository.SearchAsync(Criteria(), Ct)).TotalCount);
    }

    [Fact]
    public async Task ReplaceAsync_ExpectedVersionMatches_ReturnsDoneAndStoresReplacement()
    {
        var stored = Series();
        await _repository.AddAsync(stored, Ct);
        var replacement = stored.Replace(new TimeSeriesDefinition(
            stored.Type, stored.BiddingZone, null, stored.Unit, stored.Resolution, stored.Start, [1m, 2m]), _midnight).Value;

        var outcome = await _repository.ReplaceAsync(replacement, expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.Done, outcome);
        Assert.Equal(2, (await _repository.GetAsync(stored.Id, Ct))!.Version);
    }

    [Fact]
    public async Task ReplaceAsync_StaleExpectedVersion_ReturnsVersionMismatchAndKeepsStored()
    {
        var stored = Series();
        await _repository.AddAsync(stored, Ct);
        var replacement = stored.Replace(new TimeSeriesDefinition(
            stored.Type, stored.BiddingZone, null, stored.Unit, stored.Resolution, stored.Start, [1m]), _midnight).Value;

        var outcome = await _repository.ReplaceAsync(replacement, expectedVersion: 7, Ct);

        Assert.Equal(WriteOutcome.VersionMismatch, outcome);
        Assert.Same(stored, await _repository.GetAsync(stored.Id, Ct));
    }

    [Fact]
    public async Task ReplaceAsync_UnknownSeries_ReturnsNotFound()
    {
        var outcome = await _repository.ReplaceAsync(Series(), expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task ReplaceAsync_MovedIntoAnotherSeriesPeriod_ReturnsConflict()
    {
        var morning = Series(startHour: 0, hours: 3);
        var noon = Series(startHour: 12, hours: 3);
        await _repository.AddAsync(morning, Ct);
        await _repository.AddAsync(noon, Ct);
        var movedToNoon = morning.Replace(new TimeSeriesDefinition(
            morning.Type, morning.BiddingZone, null, morning.Unit, morning.Resolution, _midnight.AddHours(13), [1m]), _midnight).Value;

        var outcome = await _repository.ReplaceAsync(movedToNoon, expectedVersion: 1, Ct);

        Assert.Equal(WriteOutcome.Conflict, outcome);
    }

    [Fact]
    public async Task DeleteAsync_StoredSeries_ReturnsTrueAndRemovesIt()
    {
        var series = Series();
        await _repository.AddAsync(series, Ct);

        Assert.True(await _repository.DeleteAsync(series.Id, Ct));
        Assert.Null(await _repository.GetAsync(series.Id, Ct));
        Assert.False(await _repository.DeleteAsync(series.Id, Ct));
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
            await _repository.AddAsync(series, Ct);
        }

        var page = await _repository.SearchAsync(Criteria(biddingZone: BiddingZone.SE3), Ct);

        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(no1Production, page.Items);
    }

    [Fact]
    public async Task SearchAsync_ByWindow_ReturnsOnlySeriesOverlappingIt()
    {
        var morning = Series(startHour: 0, hours: 3);
        var noon = Series(startHour: 12, hours: 3);
        await _repository.AddAsync(morning, Ct);
        await _repository.AddAsync(noon, Ct);

        var page = await _repository.SearchAsync(Criteria(window: new TimeInterval(_midnight.AddHours(3), _midnight.AddHours(13))), Ct);

        Assert.Equal([noon], page.Items);
    }

    [Theory]
    [InlineData(3)]            // just past the last page
    [InlineData(int.MaxValue)] // (page - 1) * pageSize would overflow an int
    public async Task SearchAsync_PageBeyondLastPage_ReturnsNoItemsButTotalCount(int page)
    {
        await _repository.AddAsync(Series(), Ct);

        var result = await _repository.SearchAsync(Criteria(page: page, pageSize: 200), Ct);

        Assert.Empty(result.Items);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_SecondPage_ReturnsRemainingItemsAndTotalCount()
    {
        for (var day = 0; day < 5; day++)
        {
            await _repository.AddAsync(Series(startHour: day * 24, hours: 24), Ct);
        }

        var page = await _repository.SearchAsync(Criteria(page: 2, pageSize: 3), Ct);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal([_midnight.AddDays(3), _midnight.AddDays(4)], page.Items.Select(series => series.Start));
    }
}
