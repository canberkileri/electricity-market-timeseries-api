namespace MarketTimeSeries.Application.Storage;

/// <summary>One page of search results, plus the number of matches over all pages.</summary>
public sealed record SearchPage(IReadOnlyList<SeriesSummary> Items, int TotalCount);
