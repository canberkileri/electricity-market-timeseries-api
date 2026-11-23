using MarketTimeSeries.Application;
using MarketTimeSeries.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MarketTimeSeries.Infrastructure.SqlServer;

/// <summary>
/// Stores series in SQL Server. The rules that need other rows are enforced by the schema, not by locks:
/// an overlapping series violates the (identity, timestamp) key of its points, and a stale version matches
/// no row in a conditional UPDATE. Each call uses its own DbContext, so one instance serves parallel requests.
/// </summary>
internal sealed class SqlServerTimeSeriesRepository(IDbContextFactory<TimeSeriesDbContext> contextFactory)
    : ITimeSeriesRepository
{
    public async Task<TimeSeries?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var record = await context.Series
            .AsNoTracking()
            .Include(series => series.Identity)
            .Include(series => series.Points.OrderBy(point => point.Timestamp))
            .SingleOrDefaultAsync(series => series.Id == id, cancellationToken);

        return record is null ? null : ToDomain(record);
    }

    public async Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Series.AsNoTracking();
        if (criteria.Type is { } type)
        {
            var typeName = type.ToString();
            query = query.Where(series => series.Identity.Type == typeName);
        }

        if (criteria.BiddingZone is { } zone)
        {
            var zoneName = zone.ToString();
            query = query.Where(series => series.Identity.BiddingZone == zoneName || series.Identity.ToBiddingZone == zoneName);
        }

        if (criteria.Window is { } window)
        {
            var (from, to) = (window.Start.UtcDateTime, window.End.UtcDateTime);
            query = query.Where(series => series.Start < to && series.End > from);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        if (skip >= totalCount)
        {
            return new SearchPage([], totalCount);
        }

        var page = await query
            .Include(series => series.Identity)
            .OrderBy(series => series.Start)
            .ThenBy(series => series.Identity.Type)
            .ThenBy(series => series.Identity.BiddingZone)
            .ThenBy(series => series.Identity.ToBiddingZone)
            .ThenBy(series => series.Id)
            .Skip((int)skip)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        var points = criteria.Window is { } clip
            ? await PointsInWindowAsync(context, page, clip, cancellationToken)
            : null;

        return new SearchPage(
            [.. page.Select(series => ToSummary(series, points is null ? null : points[series.Id]))],
            totalCount);
    }

    public async Task<WriteOutcome> AddAsync(TimeSeries series, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var identityId = await GetOrCreateIdentityIdAsync(context, series, cancellationToken);

        context.Series.Add(ToRecord(series, identityId));
        try
        {
            // One SaveChanges is one transaction: the series and all its points, or nothing.
            await context.SaveChangesAsync(cancellationToken);
            return WriteOutcome.Done;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            return WriteOutcome.Conflict;
        }
    }

    public async Task<WriteOutcome> ReplaceAsync(TimeSeries replacement, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var stored = await context.Series
            .Where(series => series.Id == replacement.Id)
            .Select(series => new { series.IdentityId })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return WriteOutcome.NotFound;
        }

        try
        {
            // The version check and the update are one statement: a concurrent update makes it match no row.
            var updated = await context.Series
                .Where(series => series.Id == replacement.Id && series.Version == expectedVersion)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(series => series.Unit, replacement.Unit.ToString())
                        .SetProperty(series => series.Start, replacement.Start.UtcDateTime)
                        .SetProperty(series => series.End, replacement.End.UtcDateTime)
                        .SetProperty(series => series.Version, replacement.Version)
                        .SetProperty(series => series.UpdatedAt, replacement.UpdatedAt.UtcDateTime),
                    cancellationToken);
            if (updated == 0)
            {
                return WriteOutcome.VersionMismatch;
            }

            await context.Points.Where(point => point.SeriesId == replacement.Id).ExecuteDeleteAsync(cancellationToken);
            context.Points.AddRange(PointsOf(replacement, stored.IdentityId));
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return WriteOutcome.Done;
        }
        catch (Exception exception) when (IsDuplicateKey(exception))
        {
            // The new period overlaps another series; disposing the transaction rolls everything back.
            return WriteOutcome.Conflict;
        }
    }

    public async Task<WriteOutcome> DeleteAsync(Guid id, int? expectedVersion, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Series.Where(series => series.Id == id);
        if (expectedVersion is { } version)
        {
            query = query.Where(series => series.Version == version);
        }

        // Its points go with it: the foreign key cascades the delete inside the database.
        if (await query.ExecuteDeleteAsync(cancellationToken) == 1)
        {
            return WriteOutcome.Done;
        }

        return await context.Series.AnyAsync(series => series.Id == id, cancellationToken)
            ? WriteOutcome.VersionMismatch
            : WriteOutcome.NotFound;
    }

    // Reads only the window's points of the page's series. Filtering on IdentityId as well lets SQL Server seek
    // the clustered (IdentityId, Timestamp) key instead of scanning every point of each series.
    private static async Task<ILookup<Guid, Point>> PointsInWindowAsync(
        TimeSeriesDbContext context,
        List<SeriesRecord> page,
        TimeInterval window,
        CancellationToken cancellationToken)
    {
        var seriesIds = page.Select(series => series.Id).ToList();
        var identityIds = page.Select(series => series.IdentityId).Distinct().ToList();
        var (from, to) = (window.Start.UtcDateTime, window.End.UtcDateTime);

        var points = await context.Points
            .AsNoTracking()
            .Where(point => identityIds.Contains(point.IdentityId)
                && point.Timestamp >= from
                && point.Timestamp < to
                && seriesIds.Contains(point.SeriesId))
            .OrderBy(point => point.Timestamp)
            .ToListAsync(cancellationToken);

        return points.ToLookup(point => point.SeriesId, point => new Point(Utc(point.Timestamp), point.Value));
    }

    private static async Task<int> GetOrCreateIdentityIdAsync(
        TimeSeriesDbContext context,
        TimeSeries series,
        CancellationToken cancellationToken)
    {
        var identity = new SeriesIdentityRecord
        {
            Type = series.Type.ToString(),
            BiddingZone = series.BiddingZone.ToString(),
            ToBiddingZone = series.ToBiddingZone?.ToString(),
            Resolution = series.Resolution.ToString(),
        };

        if (await FindIdentityIdAsync(context, identity, cancellationToken) is { } existingId)
        {
            return existingId;
        }

        context.Identities.Add(identity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return identity.Id;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Another request created the same identity a moment earlier: use that one.
            context.ChangeTracker.Clear();
            return await FindIdentityIdAsync(context, identity, cancellationToken)
                ?? throw new InvalidOperationException("The identity was reported as a duplicate but could not be read.");
        }
    }

    private static Task<int?> FindIdentityIdAsync(
        TimeSeriesDbContext context,
        SeriesIdentityRecord identity,
        CancellationToken cancellationToken) =>
        context.Identities
            .Where(stored => stored.Type == identity.Type
                && stored.BiddingZone == identity.BiddingZone
                && stored.ToBiddingZone == identity.ToBiddingZone
                && stored.Resolution == identity.Resolution)
            .Select(stored => (int?)stored.Id)
            .SingleOrDefaultAsync(cancellationToken);

    // 2627: primary key or unique constraint; 2601: unique index. Both mean "this already exists".
    private static bool IsDuplicateKey(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2627 or 2601 })
            {
                return true;
            }
        }

        return false;
    }

    private static SeriesRecord ToRecord(TimeSeries series, int identityId) => new()
    {
        Id = series.Id,
        IdentityId = identityId,
        Unit = series.Unit.ToString(),
        Start = series.Start.UtcDateTime,
        End = series.End.UtcDateTime,
        Version = series.Version,
        CreatedAt = series.CreatedAt.UtcDateTime,
        UpdatedAt = series.UpdatedAt.UtcDateTime,
        Points = [.. PointsOf(series, identityId)],
    };

    private static IEnumerable<PointRecord> PointsOf(TimeSeries series, int identityId) =>
        series.Points.Select(point => new PointRecord
        {
            IdentityId = identityId,
            Timestamp = point.Timestamp.UtcDateTime,
            SeriesId = series.Id,
            Value = point.Value,
        });

    // Restore checks the rules again, so even a corrupt row cannot become an invalid series.
    private static TimeSeries ToDomain(SeriesRecord record) => TimeSeries.Restore(
        record.Id,
        new TimeSeriesDefinition(
            Enum.Parse<TimeSeriesType>(record.Identity.Type),
            Enum.Parse<BiddingZone>(record.Identity.BiddingZone),
            record.Identity.ToBiddingZone is { } toZone ? Enum.Parse<BiddingZone>(toZone) : null,
            Enum.Parse<MeasurementUnit>(record.Unit),
            Enum.Parse<Resolution>(record.Identity.Resolution),
            Utc(record.Start),
            [.. record.Points.Select(point => point.Value)]),
        record.Version,
        Utc(record.CreatedAt),
        Utc(record.UpdatedAt));

    private static SeriesSummary ToSummary(SeriesRecord record, IEnumerable<Point>? points) => new(
        record.Id,
        Enum.Parse<TimeSeriesType>(record.Identity.Type),
        Enum.Parse<BiddingZone>(record.Identity.BiddingZone),
        record.Identity.ToBiddingZone is { } toZone ? Enum.Parse<BiddingZone>(toZone) : null,
        Enum.Parse<MeasurementUnit>(record.Unit),
        Enum.Parse<Resolution>(record.Identity.Resolution),
        Utc(record.Start),
        Utc(record.End),
        record.Version,
        points is null ? null : [.. points]);

    // SQL Server returns datetime2 without a kind; every DateTime in this schema is UTC.
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
