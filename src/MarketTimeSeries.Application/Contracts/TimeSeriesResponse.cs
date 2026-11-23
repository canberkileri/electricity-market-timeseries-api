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
    /// <summary>A whole series with all its points (create, get, update).</summary>
    public static TimeSeriesResponse From(TimeSeries series) => new(
        series.Id,
        series.Type,
        series.BiddingZone,
        series.ToBiddingZone,
        series.Unit,
        series.Resolution,
        series.Start,
        series.End,
        series.Version,
        Map(series.Points));

    /// <summary>A search result: points only inside the window, and left out without one.</summary>
    public static TimeSeriesResponse From(SeriesSummary summary) => new(
        summary.Id,
        summary.Type,
        summary.BiddingZone,
        summary.ToBiddingZone,
        summary.Unit,
        summary.Resolution,
        summary.Start,
        summary.End,
        summary.Version,
        summary.Points is null ? null : Map(summary.Points));

    private static List<PointResponse> Map(IEnumerable<Point> points) =>
        [.. points.Select(point => new PointResponse(point.Timestamp, point.Value))];
}

public sealed record PointResponse(DateTimeOffset Timestamp, decimal Value);
