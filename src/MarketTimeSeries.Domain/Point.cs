namespace MarketTimeSeries.Domain;

/// <summary>One value with its timestamp: the start of the period the value covers.</summary>
public readonly record struct Point(DateTimeOffset Timestamp, decimal Value);
