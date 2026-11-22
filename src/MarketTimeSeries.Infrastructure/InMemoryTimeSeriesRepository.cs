using System.Collections.Concurrent;
using MarketTimeSeries.Application;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Infrastructure;

/// <summary>
/// Keeps series in memory. Reads are lock-free; every write takes one lock, so "check, then write" is atomic.
/// Series are immutable, so a write replaces a reference and readers always see a complete series.
/// </summary>
public sealed class InMemoryTimeSeriesRepository : ITimeSeriesRepository
{
    private readonly ConcurrentDictionary<Guid, TimeSeries> _series = new();
    private readonly Lock _writeLock = new();

    public Task<TimeSeries?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_series.TryGetValue(id, out var series) ? series : null);

    public Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken)
    {
        var matches = _series.Values
            .Where(series => criteria.Type is null || series.Type == criteria.Type)
            .Where(series => criteria.BiddingZone is null
                || series.BiddingZone == criteria.BiddingZone
                || series.ToBiddingZone == criteria.BiddingZone)
            .Where(series => criteria.Window is not { } window || series.Period.Overlaps(window))
            .OrderBy(series => series.Start)
            .ThenBy(series => series.Type)
            .ThenBy(series => series.BiddingZone)
            .ThenBy(series => series.ToBiddingZone)
            .ThenBy(series => series.Id) // a stable order, so paging never skips or repeats a series
            .ToList();

        // In long: (page - 1) * pageSize overflows int for very large page numbers, and a negative skip would
        // silently return the first page instead of an empty one.
        var skip = (long)(criteria.Page - 1) * criteria.PageSize;
        List<TimeSeries> page = skip >= matches.Count
            ? []
            : [.. matches.Skip((int)skip).Take(criteria.PageSize)];

        return Task.FromResult(new SearchPage(page, matches.Count));
    }

    public Task<WriteOutcome> AddAsync(TimeSeries series, CancellationToken cancellationToken)
    {
        lock (_writeLock)
        {
            if (ConflictsWithStored(series))
            {
                return Task.FromResult(WriteOutcome.Conflict);
            }

            _series[series.Id] = series;
            return Task.FromResult(WriteOutcome.Done);
        }
    }

    public Task<WriteOutcome> ReplaceAsync(TimeSeries replacement, int expectedVersion, CancellationToken cancellationToken)
    {
        lock (_writeLock)
        {
            if (!_series.TryGetValue(replacement.Id, out var stored))
            {
                return Task.FromResult(WriteOutcome.NotFound);
            }

            if (stored.Version != expectedVersion)
            {
                return Task.FromResult(WriteOutcome.VersionMismatch);
            }

            if (ConflictsWithStored(replacement))
            {
                return Task.FromResult(WriteOutcome.Conflict);
            }

            _series[replacement.Id] = replacement;
            return Task.FromResult(WriteOutcome.Done);
        }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Also under the lock: otherwise a replace that already found the series could store it again after the delete.
        lock (_writeLock)
        {
            return Task.FromResult(_series.TryRemove(id, out _));
        }
    }

    // ConflictsWith ignores a series with the same id, so a replacement is never compared with the version it replaces.
    private bool ConflictsWithStored(TimeSeries candidate) =>
        _series.Values.Any(candidate.ConflictsWith);
}
