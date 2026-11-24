namespace MarketTimeSeries.Domain;

/// <summary>A half-open time interval [Start, End): Start is included, End is not.</summary>
public readonly record struct TimeInterval
{
    public TimeInterval(DateTimeOffset start, DateTimeOffset end)
    {
        // A guard against programming errors: callers validate user input before building an interval.
        if (end <= start)
        {
            throw new ArgumentException("End must be after start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    /// <summary>Adjacent intervals, where one ends exactly when the other starts, do not overlap.</summary>
    public bool Overlaps(TimeInterval other) => Start < other.End && other.Start < End;
}
