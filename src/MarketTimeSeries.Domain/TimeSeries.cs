using System.Globalization;

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

    /// <summary>A thousandth of a MW is 1 kW, finer than any market data; storage keeps exactly this many.</summary>
    public const int MaxDecimalPlaces = 3;

    /// <summary>
    /// The largest magnitude a value may have, in MW: far above the whole Nordic system (around 100 GW), so only a
    /// mistake can exceed it, and well inside what storage can hold, so a store never fails on a value it was given.
    /// </summary>
    public const decimal MaxAbsoluteValue = 1_000_000m;

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

        // Version 7 GUIDs start with their creation time, so they sort by age where GUIDs sort byte by byte
        // (PostgreSQL). SQL Server sorts uniqueidentifier by its last bytes first, so there the GUID is not the
        // clustered key.
        var series = new TimeSeries(Guid.CreateVersion7(now), definition, version: 1, createdAt: now, updatedAt: now);

        return Result<TimeSeries>.Success(series);
    }

    /// <summary>
    /// Rebuilds a stored series, keeping its id, version and timestamps. The rules are checked again, so even
    /// storage cannot produce an invalid series: data that breaks them means corrupt storage, a bug, not a
    /// client error.
    /// </summary>
    public static TimeSeries Restore(
        Guid id,
        TimeSeriesDefinition definition,
        int version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var errors = Validate(definition);
        if (errors.Count > 0 || version < 1)
        {
            throw new InvalidOperationException(
                $"Stored time series {id} is invalid: {string.Join(" ", errors.Select(error => error.Message))}");
        }

        return new TimeSeries(id, definition, version, createdAt, updatedAt);
    }

    /// <summary>
    /// Replaces the data of the series (a PUT), checked by the same rules as <see cref="Create"/>. The identity
    /// (type, zones, resolution) is fixed: an id always describes the same thing, so its versions stay comparable.
    /// Returns a new instance that keeps <see cref="Id"/> and <see cref="CreatedAt"/>, with the version
    /// increased by one. This instance is never changed, so readers holding it are not affected.
    /// </summary>
    public Result<TimeSeries> Replace(TimeSeriesDefinition definition, DateTimeOffset now)
    {
        List<ValidationError> errors = [.. IdentityChanges(definition), .. Validate(definition)];
        if (errors.Count > 0)
        {
            return Result<TimeSeries>.Failure(errors);
        }

        var replacement = new TimeSeries(Id, definition, Version + 1, CreatedAt, updatedAt: now);

        return Result<TimeSeries>.Success(replacement);
    }

    private IEnumerable<ValidationError> IdentityChanges(TimeSeriesDefinition definition)
    {
        if (definition.Type != Type)
        {
            yield return IdentityError(nameof(Type));
        }

        if (definition.BiddingZone != BiddingZone)
        {
            yield return IdentityError(nameof(BiddingZone));
        }

        if (definition.ToBiddingZone != ToBiddingZone)
        {
            yield return IdentityError(nameof(ToBiddingZone));
        }

        if (definition.Resolution != Resolution)
        {
            yield return IdentityError(nameof(Resolution));
        }
    }

    private static ValidationError IdentityError(string field) =>
        new(field, $"{field} cannot be changed; create a new series instead.");

    /// <summary>
    /// True if both series describe the same thing (type, zones and resolution) for an overlapping period.
    /// A series never conflicts with itself, so an update can be checked against the stored version it replaces.
    /// </summary>
    public bool ConflictsWith(TimeSeries other) =>
        other.Id != Id
        && other.Type == Type
        && other.BiddingZone == BiddingZone
        && other.ToBiddingZone == ToBiddingZone
        && other.Resolution == Resolution
        && other.Period.Overlaps(Period);

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

        if (type != TimeSeriesType.CrossBorderFlow && values.Any(value => value < 0))
        {
            errors.Add(new(nameof(Values), $"{type} values must be zero or positive."));
        }

        if (values.Any(value => Math.Abs(value) > MaxAbsoluteValue))
        {
            // Invariant culture: the message must read the same whatever language the server is set to.
            errors.Add(new(nameof(Values), string.Create(
                CultureInfo.InvariantCulture,
                $"Values must be between -{MaxAbsoluteValue:N0} and {MaxAbsoluteValue:N0} MW.")));
        }

        // Rejected rather than rounded: a store must never silently change a value it was given.
        if (values.Any(value => value != decimal.Round(value, MaxDecimalPlaces)))
        {
            errors.Add(new(nameof(Values), $"Values may have at most {MaxDecimalPlaces} decimal places."));
        }

        return errors;
    }
}
