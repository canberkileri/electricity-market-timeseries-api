namespace MarketTimeSeries.Domain;

/// <summary>
/// Everything a client decides about a series: what it describes and its values. The server adds the rest
/// (id, version, timestamps). <see cref="TimeSeries.Create"/> checks it against the domain rules.
/// </summary>
public sealed record TimeSeriesDefinition(
    TimeSeriesType Type,
    BiddingZone BiddingZone,
    BiddingZone? ToBiddingZone,
    MeasurementUnit Unit,
    Resolution Resolution,
    DateTimeOffset Start,
    IReadOnlyList<decimal> Values);
