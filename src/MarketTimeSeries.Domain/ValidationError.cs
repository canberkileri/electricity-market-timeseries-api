namespace MarketTimeSeries.Domain;

/// <summary>A broken business rule, named after the property it concerns.</summary>
public sealed record ValidationError(string Field, string Message);
