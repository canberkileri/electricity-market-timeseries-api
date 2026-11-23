using Microsoft.EntityFrameworkCore;

namespace MarketTimeSeries.Infrastructure.SqlServer;

/// <summary>
/// The SQL Server schema. The business rules that need other rows are enforced by keys, not by code:
/// a unique identity per (type, zones, resolution), and a points key of (identity, timestamp), so two series
/// with the same identity can never cover the same instant.
/// </summary>
internal sealed class TimeSeriesDbContext(DbContextOptions<TimeSeriesDbContext> options) : DbContext(options)
{
    public DbSet<SeriesIdentityRecord> Identities => Set<SeriesIdentityRecord>();

    public DbSet<SeriesRecord> Series => Set<SeriesRecord>();

    public DbSet<PointRecord> Points => Set<PointRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeriesIdentityRecord>(identity =>
        {
            identity.ToTable("SeriesIdentity");
            identity.Property(x => x.Type).HasMaxLength(20).IsUnicode(false);
            identity.Property(x => x.BiddingZone).HasMaxLength(3).IsUnicode(false);
            identity.Property(x => x.ToBiddingZone).HasMaxLength(3).IsUnicode(false);
            identity.Property(x => x.Resolution).HasMaxLength(5).IsUnicode(false);

            // EF Core adds "WHERE ToBiddingZone IS NOT NULL" to unique indexes on nullable columns, which would
            // allow duplicate production and consumption identities. Without the filter, SQL Server treats NULLs
            // as equal, which is exactly the rule: one identity per (type, zones, resolution).
            identity.HasIndex(x => new { x.Type, x.BiddingZone, x.ToBiddingZone, x.Resolution })
                .IsUnique()
                .HasFilter(null);
        });

        modelBuilder.Entity<SeriesRecord>(series =>
        {
            series.ToTable("Series");

            // The GUID stays the public key but is not clustered: SQL Server does not store version 7 GUIDs in
            // creation order. Rows are clustered by identity and start, the order searches read them in.
            series.HasKey(x => x.Id).IsClustered(false);
            series.HasIndex(x => new { x.IdentityId, x.Start }).IsUnique().IsClustered();

            series.Property(x => x.Unit).HasMaxLength(5).IsUnicode(false);
            series.Property(x => x.Start).HasColumnType("datetime2(0)");
            series.Property(x => x.End).HasColumnType("datetime2(0)");
            series.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
            series.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");

            series.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
            series.HasMany(x => x.Points).WithOne().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PointRecord>(point =>
        {
            point.ToTable("Point");
            point.HasKey(x => new { x.IdentityId, x.Timestamp }).IsClustered();
            point.HasIndex(x => x.SeriesId);
            point.Property(x => x.Timestamp).HasColumnType("datetime2(0)");
            point.Property(x => x.Value).HasPrecision(18, 3);

            point.HasOne<SeriesIdentityRecord>().WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
