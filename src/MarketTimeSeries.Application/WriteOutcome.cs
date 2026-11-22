namespace MarketTimeSeries.Application;

/// <summary>Result of an atomic repository write.</summary>
public enum WriteOutcome
{
    Done,

    /// <summary>Another series with the same identity overlaps in time.</summary>
    Conflict,

    NotFound,

    /// <summary>The stored series was changed by someone else since it was read.</summary>
    VersionMismatch,
}
