using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class TimeSeriesRestoreTests
{
    private static readonly DateTimeOffset _midnight = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSeriesDefinition _definition = new(
        TimeSeriesType.Production,
        BiddingZone.SE3,
        null,
        MeasurementUnit.MW,
        Resolution.PT60M,
        _midnight,
        [100m, 110m]);

    [Fact]
    public void Restore_StoredValues_KeepsIdVersionAndTimestamps()
    {
        var id = Guid.NewGuid();
        var updatedAt = _midnight.AddDays(2);

        var series = TimeSeries.Restore(id, _definition, version: 4, createdAt: _midnight, updatedAt: updatedAt);

        Assert.Equal(id, series.Id);
        Assert.Equal(4, series.Version);
        Assert.Equal(_midnight, series.CreatedAt);
        Assert.Equal(updatedAt, series.UpdatedAt);
        Assert.Equal([100m, 110m], series.Values);
    }

    [Fact]
    public void Restore_DataBreakingTheRules_ThrowsInvalidOperationException()
    {
        // Negative production can only come from corrupt storage: a bug, not a client error.
        var corrupt = _definition with { Values = [-1m] };

        Assert.Throws<InvalidOperationException>(() => TimeSeries.Restore(Guid.NewGuid(), corrupt, 1, _midnight, _midnight));
    }

    [Fact]
    public void Restore_VersionBelowOne_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => TimeSeries.Restore(Guid.NewGuid(), _definition, 0, _midnight, _midnight));
    }
}
