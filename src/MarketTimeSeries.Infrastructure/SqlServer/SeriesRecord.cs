namespace MarketTimeSeries.Infrastructure.SqlServer;

/// <summary>One row per series: the API resource, a block of points within one identity.</summary>
internal sealed class SeriesRecord
{
    public Guid Id { get; set; }

    public int IdentityId { get; set; }

    public SeriesIdentityRecord Identity { get; set; } = null!;

    public required string Unit { get; set; }

    /// <summary>UTC, like every DateTime in this schema.</summary>
    public DateTime Start { get; set; }

    /// <summary>Stored, not calculated, so period searches can use an index.</summary>
    public DateTime End { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<PointRecord> Points { get; set; } = [];
}
