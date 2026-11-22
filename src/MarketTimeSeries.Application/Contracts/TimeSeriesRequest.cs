using System.ComponentModel.DataAnnotations;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application.Contracts;

/// <summary>
/// Body of POST and PUT. The fields are nullable so that a missing field is reported as missing,
/// instead of silently becoming the enum's first value (NO1, PT15M and so on).
/// </summary>
public sealed record TimeSeriesRequest
{
    [Required]
    public TimeSeriesType? Type { get; init; }

    [Required]
    public BiddingZone? BiddingZone { get; init; }

    public BiddingZone? ToBiddingZone { get; init; }

    [Required]
    public MeasurementUnit? Unit { get; init; }

    [Required]
    public Resolution? Resolution { get; init; }

    [Required]
    public DateTimeOffset? Start { get; init; }

    [Required]
    public IReadOnlyList<decimal>? Values { get; init; }

    /// <summary>Only called after model validation, so every required field is present.</summary>
    public TimeSeriesDefinition ToDefinition() => new(
        Type!.Value,
        BiddingZone!.Value,
        ToBiddingZone,
        Unit!.Value,
        Resolution!.Value,
        Start!.Value,
        Values!);
}
