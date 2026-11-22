using MarketTimeSeries.Application;
using MarketTimeSeries.Application.Contracts;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.UnitTests.Application;

public sealed class SearchQueryTests
{
    private const string Midnight = "2026-11-27T00:00:00Z";

    private static void AssertSingleError(SearchQuery query, string field)
    {
        var result = query.ToCriteria();

        Assert.False(result.IsSuccess);
        Assert.Equal(field, Assert.Single(result.Errors).Field);
    }

    [Fact]
    public void ToCriteria_NoParameters_ReturnsDefaultPageWithoutFilters()
    {
        var criteria = new SearchQuery().ToCriteria().Value;

        Assert.Equal(new SearchCriteria(null, null, null, SearchQuery.DefaultPage, SearchQuery.DefaultPageSize), criteria);
    }

    [Fact]
    public void ToCriteria_FiltersAndWindow_ReturnsThemAsCriteria()
    {
        var query = new SearchQuery
        {
            BiddingZone = BiddingZone.DK1,
            Type = TimeSeriesType.CrossBorderFlow,
            From = Midnight,
            To = "2026-11-28T00:00:00Z",
        };

        var criteria = query.ToCriteria().Value;

        Assert.Equal(BiddingZone.DK1, criteria.BiddingZone);
        Assert.Equal(TimeSeriesType.CrossBorderFlow, criteria.Type);
        var start = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(new TimeInterval(start, start.AddDays(1)), criteria.Window);
    }

    [Theory]
    [InlineData(Midnight, null, nameof(SearchQuery.To))]
    [InlineData(null, Midnight, nameof(SearchQuery.From))]
    public void ToCriteria_OnlyOneOfFromAndTo_ReturnsErrorOnTheMissingOne(string? from, string? to, string missingField)
    {
        AssertSingleError(new SearchQuery { From = from, To = to }, missingField);
    }

    [Theory]
    [InlineData("2026-11-27T00:00:00")]       // no offset: would depend on the server's time zone
    [InlineData("2026-11-27T01:00:00+01:00")] // same instant, but not UTC
    [InlineData("yesterday")]
    public void ToCriteria_FromNotAUtcTimestamp_ReturnsFromError(string from)
    {
        AssertSingleError(new SearchQuery { From = from, To = "2026-11-28T00:00:00Z" }, nameof(SearchQuery.From));
    }

    [Theory]
    [InlineData(Midnight)]               // equal
    [InlineData("2026-11-26T00:00:00Z")] // before
    public void ToCriteria_ToNotAfterFrom_ReturnsToError(string to)
    {
        AssertSingleError(new SearchQuery { From = Midnight, To = to }, nameof(SearchQuery.To));
    }

    [Fact]
    public void ToCriteria_WindowOfExactly31Days_ReturnsSuccess()
    {
        var result = new SearchQuery { From = Midnight, To = "2026-12-28T00:00:00Z" }.ToCriteria();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ToCriteria_WindowLongerThan31Days_ReturnsToError()
    {
        AssertSingleError(new SearchQuery { From = Midnight, To = "2026-12-28T00:15:00Z" }, nameof(SearchQuery.To));
    }

    [Theory]
    [InlineData(0, 50, nameof(SearchQuery.Page))]
    [InlineData(1, 0, nameof(SearchQuery.PageSize))]
    [InlineData(1, SearchQuery.MaxPageSize + 1, nameof(SearchQuery.PageSize))]
    public void ToCriteria_PagingOutOfRange_ReturnsErrorForThatParameter(int page, int pageSize, string field)
    {
        AssertSingleError(new SearchQuery { Page = page, PageSize = pageSize }, field);
    }

    [Fact]
    public void ToCriteria_SeveralProblems_ReturnsEveryError()
    {
        var query = new SearchQuery { From = Midnight, Page = 0, PageSize = 500 };

        var result = query.ToCriteria();

        Assert.Equal(
            [nameof(SearchQuery.To), nameof(SearchQuery.Page), nameof(SearchQuery.PageSize)],
            result.Errors.Select(error => error.Field));
    }
}
