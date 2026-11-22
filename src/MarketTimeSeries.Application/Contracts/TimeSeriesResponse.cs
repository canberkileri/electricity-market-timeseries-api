using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application.Contracts;

public sealed record TimeSeriesResponse(
    Guid Id,
    TimeSeriesType Type,
    BiddingZone BiddingZone,
    BiddingZone? ToBiddingZone,
    MeasurementUnit Unit,
    Resolution Resolution,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Version,
    IReadOnlyList<PointResponse> Points)
{
    /// <summary>Maps a series; with a window, only the points whose timestamp lies inside it are included.</summary>
    public static TimeSeriesResponse From(TimeSeries series, TimeInterval? window = null) => new(
        series.Id,
        series.Type,
        series.BiddingZone,
        series.ToBiddingZone,
        series.Unit,
        series.Resolution,
        series.Start,
        series.End,
        series.Version,
        [.. (window is { } clip ? series.PointsWithin(clip) : series.Points)
            .Select(point => new PointResponse(point.Timestamp, point.Value))]);
}

public sealed record PointResponse(DateTimeOffset Timestamp, decimal Value);
