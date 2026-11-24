using MarketTimeSeries.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace MarketTimeSeries.UnitTests.Infrastructure;

/// <summary>
/// Locks in the schema decisions by reading the CREATE script EF Core generates from the model.
/// No database is needed: the script is produced from the model alone.
/// </summary>
public sealed class SqlServerSchemaTests
{
    private static readonly string _script = CreateScript();

    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<TimeSeriesDbContext>().UseSqlServer("Server=unused").Options;
        using var context = new TimeSeriesDbContext(options);
        return context.Database.GenerateCreateScript();
    }

    /// <summary>Two series with the same identity can never store a value for the same instant: no overlaps.</summary>
    [Fact]
    public void Point_PrimaryKey_IsClusteredOnIdentityAndTimestamp()
    {
        Assert.Contains("CONSTRAINT [PK_Point] PRIMARY KEY CLUSTERED ([IdentityId], [Timestamp])", _script, StringComparison.Ordinal);
    }

    /// <summary>
    /// EF Core would add "WHERE [ToBiddingZone] IS NOT NULL", allowing duplicate production identities.
    /// </summary>
    [Fact]
    public void SeriesIdentity_UniqueIndex_HasNoNullFilter()
    {
        var index = _script.Split('\n').Single(line => line.Contains("IX_SeriesIdentity_", StringComparison.Ordinal));

        Assert.StartsWith("CREATE UNIQUE INDEX", index, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", index, StringComparison.Ordinal);
    }

    /// <summary>
    /// SQL Server does not store version 7 GUIDs in creation order, so the GUID is not the clustered key.
    /// </summary>
    [Fact]
    public void Series_GuidKey_IsNonClusteredAndRowsClusterOnIdentityAndStart()
    {
        Assert.Contains("CONSTRAINT [PK_Series] PRIMARY KEY NONCLUSTERED ([Id])", _script, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE CLUSTERED INDEX [IX_Series_IdentityId_Start] ON [Series] ([IdentityId], [Start])", _script, StringComparison.Ordinal);
    }
}
