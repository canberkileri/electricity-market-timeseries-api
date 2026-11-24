using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class TimeIntervalTests
{
    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 11, 27, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Overlaps_AdjacentIntervals_ReturnsFalse()
    {
        var first = new TimeInterval(At(0), At(1));
        var second = new TimeInterval(At(1), At(2));

        Assert.False(first.Overlaps(second));
        Assert.False(second.Overlaps(first));
    }

    [Theory]
    [InlineData(0, 30, 1, 30)] // partly overlapping
    [InlineData(0, 15, 0, 45)] // inside the other
    [InlineData(0, 0, 1, 0)]   // identical
    public void Overlaps_OverlappingIntervals_ReturnsTrue(int startHour, int startMinute, int endHour, int endMinute)
    {
        var existing = new TimeInterval(At(0), At(1));
        var other = new TimeInterval(At(startHour, startMinute), At(endHour, endMinute));

        Assert.True(existing.Overlaps(other));
        Assert.True(other.Overlaps(existing));
    }

    [Theory]
    [InlineData(1, 1)] // end equal to start
    [InlineData(2, 1)] // end before start
    public void Constructor_EndNotAfterStart_ThrowsArgumentException(int startHour, int endHour)
    {
        Assert.Throws<ArgumentException>(() => new TimeInterval(At(startHour), At(endHour)));
    }
}
