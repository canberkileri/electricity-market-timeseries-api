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
        [.. series.Points.Select(point => new PointResponse(point.Timestamp, point.Value))]);
}

public sealed record PointResponse(DateTimeOffset Timestamp, decimal Value);
