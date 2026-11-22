using System.Globalization;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>
/// The use cases. Each one combines a domain operation with storage and turns every expected failure
/// into an <see cref="Error"/>; nothing here knows about HTTP.
/// </summary>
public sealed class TimeSeriesService(ITimeSeriesRepository repository, TimeProvider timeProvider)
{
    public async Task<OperationResult<TimeSeries>> CreateAsync(TimeSeriesDefinition definition, CancellationToken cancellationToken)
    {
        var created = TimeSeries.Create(definition, timeProvider.GetUtcNow());
        if (!created.IsSuccess)
        {
            return Error.Validation(created.Errors);
        }

        var outcome = await repository.AddAsync(created.Value, cancellationToken);

        return outcome == WriteOutcome.Done ? created.Value : OverlapError(created.Value);
    }

    public async Task<OperationResult<TimeSeries>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var series = await repository.GetAsync(id, cancellationToken);

        return series is null ? Error.NotFound($"Time series {id} was not found.") : series;
    }

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
