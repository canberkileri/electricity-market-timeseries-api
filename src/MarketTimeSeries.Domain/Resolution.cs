namespace MarketTimeSeries.Domain;

/// <summary>Length of one period, named after its ISO 8601 duration.</summary>
public enum Resolution
{
    PT15M,
    PT60M,
}

public static class ResolutionExtensions
{
    public static TimeSpan ToTimeSpan(this Resolution resolution) => resolution switch
    {
        Resolution.PT15M => TimeSpan.FromMinutes(15),
        Resolution.PT60M => TimeSpan.FromMinutes(60),
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown resolution."),
    };

    /// <summary>True if the timestamp falls exactly on a period boundary, for example 00:15 but not 00:07 for PT15M.</summary>
    public static bool IsAligned(this Resolution resolution, DateTimeOffset timestamp) =>
        timestamp.UtcTicks % resolution.ToTimeSpan().Ticks == 0;
}
