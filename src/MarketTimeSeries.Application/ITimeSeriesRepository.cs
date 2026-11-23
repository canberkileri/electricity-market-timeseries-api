using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>
/// Storage for time series. Writes check the business rules that need to see other series (overlap, version)
/// and store the result in one atomic step, so two parallel requests can never both succeed.
/// The interface is async so a database implementation can replace the in-memory one without changes here.
/// </summary>
public interface ITimeSeriesRepository
{
    Task<TimeSeries?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Series matching every given filter, ordered by start, type and zones, and paged. Each result carries only the
    /// points inside the window, and none when there is no window.
    /// </summary>
    Task<SearchPage> SearchAsync(SearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Adds the series unless it conflicts with a stored one: <see cref="WriteOutcome.Done"/> or <see cref="WriteOutcome.Conflict"/>.</summary>
    Task<WriteOutcome> AddAsync(TimeSeries series, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the replacement if the stored series still has <paramref name="expectedVersion"/> and the
    /// replacement conflicts with no other series. Any outcome except <see cref="WriteOutcome.Done"/> changes nothing.
    /// </summary>
    Task<WriteOutcome> ReplaceAsync(TimeSeries replacement, int expectedVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the series if it exists and, when <paramref name="expectedVersion"/> is given, still has that version:
    /// <see cref="WriteOutcome.Done"/>, <see cref="WriteOutcome.NotFound"/> or <see cref="WriteOutcome.VersionMismatch"/>.
    /// </summary>
    Task<WriteOutcome> DeleteAsync(Guid id, int? expectedVersion, CancellationToken cancellationToken);
}
