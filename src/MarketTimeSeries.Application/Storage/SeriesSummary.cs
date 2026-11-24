using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application.Storage;

/// <summary>
/// A search result: the header of a series plus only the points inside the search window (none without a window).
/// Reads use this instead of the TimeSeries aggregate, because an aggregate needs all its values: a database can
/// return just the window's points far more cheaply than rebuilding whole series only to clip them.
/// </summary>
public sealed record SeriesSummary(
    Guid Id,
    TimeSeriesType Type,
    BiddingZone BiddingZone,
    BiddingZone? ToBiddingZone,
    MeasurementUnit Unit,
    Resolution Resolution,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Version,
    IReadOnlyList<Point>? Points);
