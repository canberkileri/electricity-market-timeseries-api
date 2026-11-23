namespace MarketTimeSeries.Infrastructure.SqlServer;

/// <summary>One row per thing a series can describe: type, zones and resolution.</summary>
internal sealed class SeriesIdentityRecord
{
    public int Id { get; set; }

    public required string Type { get; set; }

    public required string BiddingZone { get; set; }

    public string? ToBiddingZone { get; set; }

    public required string Resolution { get; set; }
}
