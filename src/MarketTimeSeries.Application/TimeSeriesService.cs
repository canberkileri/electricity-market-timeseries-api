using System.Globalization;
using MarketTimeSeries.Application.Contracts;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>
/// The use cases. Each one combines a domain operation with storage, turns every expected failure into an
/// <see cref="Error"/> and returns the response contract; nothing here knows about HTTP.
/// </summary>
public sealed class TimeSeriesService(ITimeSeriesRepository repository, TimeProvider timeProvider)
{
    public async Task<OperationResult<TimeSeriesResponse>> CreateAsync(TimeSeriesDefinition definition, CancellationToken cancellationToken)
    {
        var created = TimeSeries.Create(definition, timeProvider.GetUtcNow());
        if (!created.IsSuccess)
        {
            return Error.Validation(created.Errors);
        }

        var outcome = await repository.AddAsync(created.Value, cancellationToken);

        return outcome == WriteOutcome.Done ? TimeSeriesResponse.From(created.Value) : OverlapError(created.Value);
    }

    public async Task<OperationResult<TimeSeriesResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var series = await repository.GetAsync(id, cancellationToken);

        return series is null ? NotFoundError(id) : TimeSeriesResponse.From(series);
    }

    public async Task<OperationResult<PagedResponse<TimeSeriesResponse>>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var criteria = query.ToCriteria();
        if (!criteria.IsSuccess)
        {
            return Error.Validation(criteria.Errors);
        }

        var page = await repository.SearchAsync(criteria.Value, cancellationToken);
        var window = criteria.Value.Window;

        // Points only for a window (at most 31 days); without one, a page could hold years of data.
        return new PagedResponse<TimeSeriesResponse>(
            [.. page.Items.Select(series => window is { } clip
                ? TimeSeriesResponse.From(series, clip)
                : TimeSeriesResponse.WithoutPoints(series))],
            criteria.Value.Page,
            criteria.Value.PageSize,
            page.TotalCount);
    }

    /// <summary>
    /// Replaces a series (PUT). <paramref name="expectedVersion"/> comes from If-Match; null means "any version".
    /// The repository checks the version again when it writes, so an update made by someone else between the
    /// read and the write is never lost, even when the client sent no If-Match.
    /// </summary>
    public async Task<OperationResult<TimeSeriesResponse>> UpdateAsync(
        Guid id,
        TimeSeriesDefinition definition,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(id, cancellationToken);
        if (current is null)
        {
            return NotFoundError(id);
        }

        if (expectedVersion is { } expected && expected != current.Version)
        {
            return Error.PreconditionFailed(
                $"If-Match is version {expected}, but the current version is {current.Version}. Read the series again and retry.");
        }

        var replaced = current.Replace(definition, timeProvider.GetUtcNow());
        if (!replaced.IsSuccess)
        {
            return Error.Validation(replaced.Errors);
        }

        return await repository.ReplaceAsync(replaced.Value, current.Version, cancellationToken) switch
        {
            WriteOutcome.Done => TimeSeriesResponse.From(replaced.Value),
            WriteOutcome.NotFound => NotFoundError(id), // deleted after we read it
            WriteOutcome.VersionMismatch => Error.PreconditionFailed(
                "The series was changed by another request while this update ran. Read it again and retry."),
            WriteOutcome.Conflict => OverlapError(replaced.Value),
            _ => throw new InvalidOperationException("Unknown write outcome."),
        };
    }

    public async Task<OperationResult<Deleted>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(id, cancellationToken) ? new Deleted() : NotFoundError(id);

    private static Error NotFoundError(Guid id) => Error.NotFound($"Time series {id} was not found.");

    private static Error OverlapError(TimeSeries series)
    {
        var zones = series.ToBiddingZone is { } toZone ? $"{series.BiddingZone} to {toZone}" : $"{series.BiddingZone}";

        return Error.Conflict(
            $"A {series.Type} series for {zones} at {series.Resolution} already covers part of " +
            $"{Format(series.Start)} to {Format(series.End)}.");
    }

    private static string Format(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
