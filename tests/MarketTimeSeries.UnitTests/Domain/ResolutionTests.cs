using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Domain;

public sealed class ResolutionTests
{
    private static DateTimeOffset At(int hour, int minute) => new(2026, 11, 27, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Resolution.PT15M, 15)]
    [InlineData(Resolution.PT60M, 60)]
    public void ToTimeSpan_KnownResolution_ReturnsDurationInItsName(Resolution resolution, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), resolution.ToTimeSpan());
    }

    [Theory]
    [InlineData(Resolution.PT15M, 0, 0)]
    [InlineData(Resolution.PT15M, 0, 15)]
    [InlineData(Resolution.PT15M, 23, 45)]
    [InlineData(Resolution.PT60M, 1, 0)]
    public void IsAligned_TimestampOnPeriodBoundary_ReturnsTrue(Resolution resolution, int hour, int minute)
    {
        Assert.True(resolution.IsAligned(At(hour, minute)));
    }

    [Theory]
    [InlineData(Resolution.PT15M, 0, 7)]
    [InlineData(Resolution.PT60M, 0, 15)]
    public void IsAligned_TimestampBetweenPeriodBoundaries_ReturnsFalse(Resolution resolution, int hour, int minute)
    {
        Assert.False(resolution.IsAligned(At(hour, minute)));
    }
}
