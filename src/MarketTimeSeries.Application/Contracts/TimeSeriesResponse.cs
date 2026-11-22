using System.Text.Json.Serialization;
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PointResponse>? Points)
{
    /// <summary>The series with all its points.</summary>
    public static TimeSeriesResponse From(TimeSeries series) => Map(series, series.Points);

    /// <summary>The series with only the points whose timestamp lies inside the window.</summary>
    public static TimeSeriesResponse From(TimeSeries series, TimeInterval window) => Map(series, series.PointsWithin(window));

    /// <summary>
    /// The series without points, for searches without a window: a year at 15-minute resolution is
    /// 35,136 points, so a page of such series would be hundreds of megabytes.
    /// </summary>
    public static TimeSeriesResponse WithoutPoints(TimeSeries series) => Map(series, points: null);

    private static TimeSeriesResponse Map(TimeSeries series, IEnumerable<Point>? points) => new(
        series.Id,
        series.Type,
        series.BiddingZone,
        series.ToBiddingZone,
        series.Unit,
        series.Resolution,
        series.Start,
        series.End,
        series.Version,
        points is null ? null : [.. points.Select(point => new PointResponse(point.Timestamp, point.Value))]);
}

public sealed record PointResponse(DateTimeOffset Timestamp, decimal Value);
