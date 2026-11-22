namespace MarketTimeSeries.Domain;

/// <summary>
/// A regular series of values for one bidding zone (or one border, for flows): a header plus
/// <see cref="Start"/>, <see cref="Resolution"/> and one value per period. Immutable; the only way
/// to get an instance is <see cref="Create"/>, so an invalid series cannot exist.
/// </summary>
public sealed class TimeSeries
{
    /// <summary>One leap year at 15-minute resolution: 366 days * 96 periods.</summary>
    public const int MaxValues = 35_136;

    private TimeSeries(Guid id, TimeSeriesDefinition definition, int version, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        Type = definition.Type;
        BiddingZone = definition.BiddingZone;
        ToBiddingZone = definition.ToBiddingZone;
        Unit = definition.Unit;
        Resolution = definition.Resolution;
        Start = definition.Start;
        Values = Array.AsReadOnly(definition.Values.ToArray()); // a private copy: the caller's list cannot change the series
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public TimeSeriesType Type { get; }

    /// <summary>The zone of the series; for a cross-border flow, the "from" zone.</summary>
    public BiddingZone BiddingZone { get; }

    /// <summary>The "to" zone of a cross-border flow; null for every other type.</summary>
    public BiddingZone? ToBiddingZone { get; }

    public MeasurementUnit Unit { get; }

    public Resolution Resolution { get; }

    /// <summary>UTC start of the first period, aligned to the resolution.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>One value per period, in order.</summary>
    public IReadOnlyList<decimal> Values { get; }

    /// <summary>Starts at 1 and increases on every update; exposed as the ETag.</summary>
    public int Version { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    /// <summary>End of the last period (exclusive): <c>Start + Values.Count * Resolution</c>.</summary>
    public DateTimeOffset End => Start + (Values.Count * Resolution.ToTimeSpan());

    /// <summary>The half-open interval [Start, End) the series covers.</summary>
    public TimeInterval Period => new(Start, End);

    /// <summary>Every value with its timestamp: point <c>i</c> is at <c>Start + i * Resolution</c>.</summary>
    public IEnumerable<Point> Points => PointsBetween(0, Values.Count);

    /// <summary>
    /// Only the points whose timestamp lies inside the half-open window [Start, End). The index range is
    /// calculated directly, so clipping a year-long series to one day touches only that day's values.
    /// </summary>
    public IEnumerable<Point> PointsWithin(TimeInterval window) =>
        PointsBetween(FirstIndexAtOrAfter(window.Start), FirstIndexAtOrAfter(window.End));

    /// <summary>
    /// Creates a new series at version 1, or returns every broken rule. <paramref name="now"/> is passed in
    /// rather than read from the clock, so the domain stays deterministic and easy to test.
    /// </summary>
    public static Result<TimeSeries> Create(TimeSeriesDefinition definition, DateTimeOffset now)
    {
        var errors = Validate(definition);
        if (errors.Count > 0)
        {
            return Result<TimeSeries>.Failure(errors);
        }

        // Version 7 GUIDs are ordered by creation time, which keeps database indexes compact later.
        var series = new TimeSeries(Guid.CreateVersion7(now), definition, version: 1, createdAt: now, updatedAt: now);

        return Result<TimeSeries>.Success(series);
    }

    /// <summary>Index of the first point at or after the instant, limited to 0..Values.Count.</summary>
    private int FirstIndexAtOrAfter(DateTimeOffset instant)
    {
        var ticksFromStart = (instant - Start).Ticks;
        if (ticksFromStart <= 0)
        {
            return 0;
        }

        var ticksPerPeriod = Resolution.ToTimeSpan().Ticks;
        var index = (ticksFromStart + ticksPerPeriod - 1) / ticksPerPeriod; // integer division, rounded up

        return (int)Math.Min(index, Values.Count);
    }

    private IEnumerable<Point> PointsBetween(int first, int endExclusive)
    {
        var period = Resolution.ToTimeSpan();
        for (var index = first; index < endExclusive; index++)
        {
            yield return new Point(Start + (index * period), Values[index]);
        }
    }

    private static List<ValidationError> Validate(TimeSeriesDefinition definition)
    {
        var (type, biddingZone, toBiddingZone, _, resolution, start, values) = definition;
        var errors = new List<ValidationError>();

        if (start.Offset != TimeSpan.Zero)
        {
            errors.Add(new(nameof(Start), "Start must be in UTC (offset +00:00)."));
        }
        else if (!resolution.IsAligned(start))
        {
            errors.Add(new(nameof(Start), $"Start must be aligned to the resolution ({resolution})."));
        }

        if (type == TimeSeriesType.CrossBorderFlow)
        {
            if (toBiddingZone is null)
            {
                errors.Add(new(nameof(ToBiddingZone), "ToBiddingZone is required for CrossBorderFlow."));
            }
            else if (toBiddingZone == biddingZone)
            {
                errors.Add(new(nameof(ToBiddingZone), "ToBiddingZone must differ from BiddingZone."));
            }
        }
        else if (toBiddingZone is not null)
        {
            errors.Add(new(nameof(ToBiddingZone), $"ToBiddingZone must be empty for {type}."));
        }

        if (values.Count == 0)
        {
            errors.Add(new(nameof(Values), "At least one value is required."));
        }
        else if (values.Count > MaxValues)
        {
            errors.Add(new(nameof(Values), $"At most {MaxValues} values are allowed."));
        }

        // Flows may be negative (reverse direction); production and consumption may not.
        if (type != TimeSeriesType.CrossBorderFlow && values.Any(value => value < 0))
        {
            errors.Add(new(nameof(Values), $"{type} values must be zero or positive."));
        }

        return errors;
    }
}
