using MarketTimeSeries.Api.Errors;
using MarketTimeSeries.Application;
using MarketTimeSeries.Application.Contracts;
using MarketTimeSeries.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MarketTimeSeries.Api.Controllers;

[ApiController]
[Route("api/v1/time-series")]
public sealed class TimeSeriesController(TimeSeriesService service) : ControllerBase
{
    private const string Json = "application/json";
    private const string ProblemJson = "application/problem+json";

    [HttpPost]
    [EndpointSummary("Create a time series")]
    [ProducesResponseType<TimeSeriesResponse>(StatusCodes.Status201Created, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    public async Task<IActionResult> Create(TimeSeriesRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.ToDefinition(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error);
        }

        SetETag(result.Value);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get a time series by id")]
    [ProducesResponseType<TimeSeriesResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error);
        }

        SetETag(result.Value);
        return Ok(result.Value);
    }

    [HttpGet]
    [EndpointSummary("Search time series by bidding zone, type and time window")]
    [ProducesResponseType<PagedResponse<TimeSeriesResponse>>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    public async Task<IActionResult> Search(
        BiddingZone? biddingZone,
        TimeSeriesType? type,
        string? from,
        string? to,
        int page = SearchQuery.DefaultPage,
        int pageSize = SearchQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        // Named parameters give the query string the same camelCase names as the rest of the API.
        var query = new SearchQuery
        {
            BiddingZone = biddingZone,
            Type = type,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize,
        };

        var result = await service.SearchAsync(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToActionResult(result.Error);
    }

    [HttpPut("{id:guid}")]
    [EndpointSummary("Replace a time series")]
    [ProducesResponseType<TimeSeriesResponse>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, ProblemJson)]
    public async Task<IActionResult> Update(
        Guid id,
        TimeSeriesRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!ETags.TryParseIfMatch(ifMatch, out var expectedVersion))
        {
            return InvalidIfMatch();
        }

        var result = await service.UpdateAsync(id, request.ToDefinition(), expectedVersion, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error);
        }

        SetETag(result.Value);
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Delete a time series")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed, ProblemJson)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!ETags.TryParseIfMatch(ifMatch, out var expectedVersion))
        {
            return InvalidIfMatch();
        }

        var result = await service.DeleteAsync(id, expectedVersion, cancellationToken);

        return result.IsSuccess ? NoContent() : this.ToActionResult(result.Error);
    }

    private ObjectResult InvalidIfMatch() => Problem(
        "If-Match must be the ETag of the series, for example \"1\", or *.",
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid If-Match header");

    private void SetETag(TimeSeriesResponse series) => Response.Headers.ETag = ETags.Format(series.Version);
}
