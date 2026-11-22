using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class TimeSeriesPointsTests
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    // Eight quarter-hours from 00:00 to 02:00; each value equals its index, so results are easy to read.
    private static readonly TimeSeries _series = TimeSeries.Create(
        TimeSeriesType.Production,
        BiddingZone.SE3,
        toBiddingZone: null,
        MeasurementUnit.MW,
        Resolution.PT15M,
        _midnight,
        [0m, 1m, 2m, 3m, 4m, 5m, 6m, 7m],
        now: _midnight).Value;

    private static TimeInterval Window(int startMinutes, int endMinutes) =>
        new(_midnight.AddMinutes(startMinutes), _midnight.AddMinutes(endMinutes));

    [Fact]
    public void Points_PT15MSeries_ReturnsTimestampsOneResolutionApart()
    {
        var points = _series.Points.ToList();

        Assert.Equal(8, points.Count);
        Assert.Equal(new Point(_midnight, 0m), points[0]);
        Assert.Equal(new Point(_midnight.AddMinutes(15), 1m), points[1]);
        Assert.Equal(new Point(_midnight.AddMinutes(105), 7m), points[7]);
    }

    [Fact]
    public void PointsWithin_WindowInsideSeries_ReturnsOnlyPointsInWindow()
    {
        var points = _series.PointsWithin(Window(30, 75));

        Assert.Equal([2m, 3m, 4m], points.Select(point => point.Value));
    }

    [Fact]
    public void PointsWithin_PointOnWindowEnd_ExcludesThatPoint()
    {
        var points = _series.PointsWithin(Window(0, 30)).ToList();

        Assert.Equal(2, points.Count);
        Assert.Equal(_midnight.AddMinutes(15), points[^1].Timestamp);
    }

    [Fact]
    public void PointsWithin_WindowStartBetweenTimestamps_StartsAtNextPoint()
    {
        // The 00:00 point is not inside a window starting at 00:07, so the first point is 00:15.
        var points = _series.PointsWithin(Window(7, 45));

        Assert.Equal([1m, 2m], points.Select(point => point.Value));
    }

    [Fact]
    public void PointsWithin_WindowLargerThanSeries_ReturnsAllPoints()
    {
        var points = _series.PointsWithin(Window(-60, 180));

        Assert.Equal(_series.Points, points);
    }

    [Theory]
    [InlineData(-60, 0)]   // ends exactly when the series starts
    [InlineData(120, 180)] // starts exactly when the series ends
    public void PointsWithin_WindowOutsideSeries_ReturnsNoPoints(int startMinutes, int endMinutes)
    {
        var points = _series.PointsWithin(Window(startMinutes, endMinutes));

        Assert.Empty(points);
    }
}
