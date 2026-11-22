using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>
/// Already validated search filters; null means "no filter". <see cref="BiddingZone"/> matches a series at
/// either end of a flow, and <see cref="Window"/> matches series whose period overlaps it.
/// </summary>
public sealed record SearchCriteria(
    BiddingZone? BiddingZone,
    TimeSeriesType? Type,
    TimeInterval? Window,
    int Page,
    int PageSize);
