namespace MarketTimeSeries.Application.Contracts;

/// <summary>One page of results; <see cref="TotalCount"/> counts the matches over all pages.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
