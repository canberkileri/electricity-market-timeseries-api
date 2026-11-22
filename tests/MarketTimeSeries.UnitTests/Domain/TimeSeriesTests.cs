using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class TimeSeriesTests
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    // Builds a valid SE3 production series by default; each test changes only what it is about.
    private static Result<TimeSeries> Create(
        TimeSeriesType type = TimeSeriesType.Production,
        BiddingZone biddingZone = BiddingZone.SE3,
        BiddingZone? toBiddingZone = null,
        Resolution resolution = Resolution.PT60M,
        DateTimeOffset? start = null,
        decimal[]? values = null) =>
        TimeSeries.Create(
            new TimeSeriesDefinition(
                type,
                biddingZone,
                toBiddingZone,
                MeasurementUnit.MW,
                resolution,
                start ?? _midnight,
                values ?? [100m, 110m, 120m]),
            now: _midnight);

    private static void AssertSingleError(Result<TimeSeries> result, string field)
    {
        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void Create_ValidProduction_ReturnsSeriesAtVersion1()
    {
        var result = Create();

        Assert.True(result.IsSuccess);
        var series = result.Value;
        Assert.NotEqual(Guid.Empty, series.Id);
        Assert.Equal(1, series.Version);
        Assert.Equal(_midnight, series.CreatedAt);
        Assert.Equal(_midnight, series.UpdatedAt);
        Assert.Equal([100m, 110m, 120m], series.Values);
    }

    [Fact]
    public void Create_StartNotAlignedToResolution_ReturnsStartError()
    {
        var result = Create(resolution: Resolution.PT15M, start: _midnight.AddMinutes(7));

        AssertSingleError(result, nameof(TimeSeries.Start));
    }

    [Fact]
    public void Create_StartWithNonUtcOffset_ReturnsStartError()
    {
        // 01:00+01:00 is the same instant as 00:00 UTC, but only UTC is accepted.
        var result = Create(start: new DateTimeOffset(2026, 11, 27, 1, 0, 0, TimeSpan.FromHours(1)));

        AssertSingleError(result, nameof(TimeSeries.Start));
    }

    [Fact]
    public void Create_CrossBorderFlowWithoutToBiddingZone_ReturnsToBiddingZoneError()
    {
        var result = Create(type: TimeSeriesType.CrossBorderFlow, biddingZone: BiddingZone.DK1, toBiddingZone: null);

        AssertSingleError(result, nameof(TimeSeries.ToBiddingZone));
    }

    [Fact]
    public void Create_CrossBorderFlowToSameZone_ReturnsToBiddingZoneError()
    {
        var result = Create(type: TimeSeriesType.CrossBorderFlow, biddingZone: BiddingZone.DK1, toBiddingZone: BiddingZone.DK1);

        AssertSingleError(result, nameof(TimeSeries.ToBiddingZone));
    }

    [Theory]
    [InlineData(TimeSeriesType.Production)]
    [InlineData(TimeSeriesType.Consumption)]
    public void Create_ToBiddingZoneOnNonFlowType_ReturnsToBiddingZoneError(TimeSeriesType type)
    {
        var result = Create(type: type, toBiddingZone: BiddingZone.SE4);

        AssertSingleError(result, nameof(TimeSeries.ToBiddingZone));
    }

    [Theory]
    [InlineData(TimeSeriesType.Production)]
    [InlineData(TimeSeriesType.Consumption)]
    public void Create_NegativeValueOnNonFlowType_ReturnsValuesError(TimeSeriesType type)
    {
        var result = Create(type: type, values: [100m, -1m]);

        AssertSingleError(result, nameof(TimeSeries.Values));
    }

    [Fact]
    public void Create_NegativeCrossBorderFlow_ReturnsSuccess()
    {
        // A negative flow means power flows from SE3 to DK1, the reverse of the series direction.
        var result = Create(
            type: TimeSeriesType.CrossBorderFlow,
            biddingZone: BiddingZone.DK1,
            toBiddingZone: BiddingZone.SE3,
            values: [420.5m, -150m]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_NoValues_ReturnsValuesError()
    {
        var result = Create(values: []);

        AssertSingleError(result, nameof(TimeSeries.Values));
    }

    [Fact]
    public void Create_ExactlyMaxValues_ReturnsSuccess()
    {
        var result = Create(resolution: Resolution.PT15M, values: new decimal[TimeSeries.MaxValues]);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_MoreThanMaxValues_ReturnsValuesError()
    {
        var result = Create(resolution: Resolution.PT15M, values: new decimal[TimeSeries.MaxValues + 1]);

        AssertSingleError(result, nameof(TimeSeries.Values));
    }

    [Fact]
    public void Create_SeveralBrokenRules_ReturnsEveryError()
    {
        var result = Create(
            type: TimeSeriesType.CrossBorderFlow,
            toBiddingZone: null,
            start: _midnight.AddMinutes(30),
            values: []);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            [nameof(TimeSeries.Start), nameof(TimeSeries.ToBiddingZone), nameof(TimeSeries.Values)],
            result.Errors.Select(error => error.Field));
    }

    [Fact]
    public void Create_SourceListChangedAfterwards_KeepsOriginalValues()
    {
        decimal[] values = [100m, 110m];

        var series = Create(values: values).Value;
        values[0] = 999m;

        Assert.Equal(100m, series.Values[0]);
    }

    [Theory]
    [InlineData(Resolution.PT15M, 4, 1)] // 4 quarter-hours end at 01:00
    [InlineData(Resolution.PT60M, 3, 3)] // 3 hours end at 03:00
    public void End_SeriesWithValues_IsStartPlusValueCountTimesResolution(Resolution resolution, int valueCount, int expectedEndHour)
    {
        var series = Create(resolution: resolution, values: new decimal[valueCount]).Value;

        Assert.Equal(_midnight.AddHours(expectedEndHour), series.End);
        Assert.Equal(new TimeInterval(_midnight, _midnight.AddHours(expectedEndHour)), series.Period);
    }

    [Fact]
    public void Value_FailedResult_ThrowsInvalidOperationException()
    {
        var result = Create(values: []);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
