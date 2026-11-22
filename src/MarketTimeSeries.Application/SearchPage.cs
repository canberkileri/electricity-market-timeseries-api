using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>One page of search results, plus the number of matches over all pages.</summary>
public sealed record SearchPage(IReadOnlyList<TimeSeries> Items, int TotalCount);
