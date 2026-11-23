namespace MarketTimeSeries.Infrastructure.SqlServer;

/// <summary>One row per value. The key (IdentityId, Timestamp) is what makes overlapping series impossible.</summary>
internal sealed class PointRecord
{
    public int IdentityId { get; set; }

    public DateTime Timestamp { get; set; }

    public Guid SeriesId { get; set; }

    public decimal Value { get; set; }
}
