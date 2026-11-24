using System.Globalization;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application.Contracts;

/// <summary>Query string of the search endpoint. Every filter is optional.</summary>
public sealed record SearchQuery
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(31);

    /// <summary>Matches a series in this zone, or a flow with this zone at either end.</summary>
    public BiddingZone? BiddingZone { get; init; }

    public TimeSeriesType? Type { get; init; }

    /// <summary>
    /// Start of the half-open window [From, To), as an ISO 8601 UTC timestamp such as 2026-11-27T00:00:00Z.
    /// Must be given together with <see cref="To"/>. Kept as text so a timestamp without an offset is rejected
    /// on every server, instead of being read as that server's local time.
    /// </summary>
    public string? From { get; init; }

    public string? To { get; init; }

    public int Page { get; init; } = DefaultPage;

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Checks the query and turns it into repository criteria, or returns every problem found.</summary>
    public Result<SearchCriteria> ToCriteria()
    {
        var errors = new List<ValidationError>();
        TimeInterval? window = null;

        if (From is null != To is null)
        {
            errors.Add(new(From is null ? nameof(From) : nameof(To), "From and To must be given together."));
        }
        else if (From is not null && To is not null)
        {
            var from = ParseUtc(From, nameof(From), errors);
            var to = ParseUtc(To, nameof(To), errors);

            if (from is { } start && to is { } end)
            {
                if (start >= end)
                {
                    errors.Add(new(nameof(To), "To must be after From."));
                }
                else if (end - start > MaxWindow)
                {
                    errors.Add(new(nameof(To), $"The window from From to To can be at most {MaxWindow.TotalDays} days."));
                }
                else
                {
                    window = new TimeInterval(start, end);
                }
            }
        }

        if (Page < 1)
        {
            errors.Add(new(nameof(Page), "Page must be 1 or greater."));
        }

        if (PageSize is < 1 or > MaxPageSize)
        {
            errors.Add(new(nameof(PageSize), $"PageSize must be between 1 and {MaxPageSize}."));
        }

        return errors.Count > 0
            ? Result<SearchCriteria>.Failure(errors)
            : Result<SearchCriteria>.Success(new SearchCriteria(BiddingZone, Type, window, Page, PageSize));
    }

    /// <summary>
    /// Same rule as timestamps in request bodies: an explicit offset is required, and it must be UTC.
    /// </summary>
    private static DateTimeOffset? ParseUtc(string text, string field, List<ValidationError> errors)
    {
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && parsed.Kind != DateTimeKind.Unspecified
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            && instant.Offset == TimeSpan.Zero)
        {
            return instant;
        }

        errors.Add(new(field, $"{field} must be a UTC timestamp, for example 2026-11-27T00:00:00Z."));
        return null;
    }
}
