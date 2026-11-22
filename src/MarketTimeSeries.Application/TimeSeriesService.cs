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

        return new PagedResponse<TimeSeriesResponse>(
            [.. page.Items.Select(series => TimeSeriesResponse.From(series, criteria.Value.Window))],
            criteria.Value.Page,
            criteria.Value.PageSize,
            page.TotalCount);
    }

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
