using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class TimeSeriesUpdateAndOverlapTests
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    // A DK1 to SE3 hourly flow; each test changes only what it is about.
    private static TimeSeries Flow(
        BiddingZone toBiddingZone = BiddingZone.SE3,
        Resolution resolution = Resolution.PT60M,
        int startHour = 0,
        int hours = 3) =>
        TimeSeries.Create(
            new TimeSeriesDefinition(
                TimeSeriesType.CrossBorderFlow,
                BiddingZone.DK1,
                toBiddingZone,
                MeasurementUnit.MW,
                resolution,
                _midnight.AddHours(startHour),
                new decimal[hours * (resolution == Resolution.PT15M ? 4 : 1)]),
            now: _midnight).Value;

    // Same series, new values: what a PUT that only corrects data looks like.
    private static Result<TimeSeries> ReplaceValues(TimeSeries series, decimal[] values, DateTimeOffset now) =>
        series.Replace(
            new TimeSeriesDefinition(
                series.Type,
                series.BiddingZone,
                series.ToBiddingZone,
                series.Unit,
                series.Resolution,
                series.Start,
                values),
            now);

    [Fact]
    public void ConflictsWith_SameIdentityAndOverlappingPeriod_ReturnsTrue()
    {
        var existing = Flow(startHour: 0, hours: 3);
        var incoming = Flow(startHour: 2, hours: 3);

        Assert.True(incoming.ConflictsWith(existing));
    }

    [Fact]
    public void ConflictsWith_AdjacentPeriod_ReturnsFalse()
    {
        var existing = Flow(startHour: 0, hours: 3);
        var incoming = Flow(startHour: 3, hours: 3);

        Assert.False(incoming.ConflictsWith(existing));
    }

    [Fact]
    public void ConflictsWith_DifferentToBiddingZone_ReturnsFalse()
    {
        var existing = Flow(toBiddingZone: BiddingZone.SE3);
        var incoming = Flow(toBiddingZone: BiddingZone.NO2);

        Assert.False(incoming.ConflictsWith(existing));
    }

    [Fact]
    public void ConflictsWith_DifferentResolution_ReturnsFalse()
    {
        var existing = Flow(resolution: Resolution.PT60M);
        var incoming = Flow(resolution: Resolution.PT15M);

        Assert.False(incoming.ConflictsWith(existing));
    }

    [Fact]
    public void ConflictsWith_ReplacementOfSameSeries_ReturnsFalse()
    {
        var stored = Flow();
        var replacement = ReplaceValues(stored, [1m, 2m, 3m], _midnight.AddHours(1)).Value;

        Assert.False(replacement.ConflictsWith(stored));
    }

    [Fact]
    public void Replace_ValidFields_ReturnsNewVersionWithSameIdAndCreatedAt()
    {
        var original = Flow();
        var later = _midnight.AddHours(5);

        var replacement = ReplaceValues(original, [10m, 20m], later).Value;

        Assert.Equal(original.Id, replacement.Id);
        Assert.Equal(original.CreatedAt, replacement.CreatedAt);
        Assert.Equal(2, replacement.Version);
        Assert.Equal(later, replacement.UpdatedAt);
        Assert.Equal([10m, 20m], replacement.Values);
    }

    [Fact]
    public void Replace_InvalidFields_ReturnsErrorsAndLeavesOriginalUnchanged()
    {
        var original = Flow();

        // Start is not aligned to the hourly resolution.
        var result = original.Replace(Definition(original) with { Start = _midnight.AddMinutes(7) }, _midnight.AddHours(1));

        Assert.False(result.IsSuccess);
        Assert.Equal(nameof(TimeSeries.Start), Assert.Single(result.Errors).Field);
        Assert.Equal(1, original.Version);
        Assert.Equal(_midnight, original.Start);
    }

    [Theory]
    [InlineData(nameof(TimeSeries.Type))]
    [InlineData(nameof(TimeSeries.BiddingZone))]
    [InlineData(nameof(TimeSeries.ToBiddingZone))]
    [InlineData(nameof(TimeSeries.Resolution))]
    public void Replace_ChangedIdentityField_ReturnsErrorForThatField(string field)
    {
        var original = Flow();
        var changed = field switch
        {
            nameof(TimeSeries.Type) => Definition(original) with { Type = TimeSeriesType.Production, ToBiddingZone = null },
            nameof(TimeSeries.BiddingZone) => Definition(original) with { BiddingZone = BiddingZone.DK2 },
            nameof(TimeSeries.ToBiddingZone) => Definition(original) with { ToBiddingZone = BiddingZone.NO2 },
            _ => Definition(original) with { Resolution = Resolution.PT15M },
        };

        var result = original.Replace(changed, _midnight.AddHours(1));

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Field == field && error.Message.Contains("cannot be changed", StringComparison.Ordinal));
    }

    private static TimeSeriesDefinition Definition(TimeSeries series) => new(
        series.Type,
        series.BiddingZone,
        series.ToBiddingZone,
        series.Unit,
        series.Resolution,
        series.Start,
        series.Values);
}
