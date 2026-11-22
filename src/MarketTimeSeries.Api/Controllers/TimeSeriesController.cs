using MarketTimeSeries.Api.Http;
using MarketTimeSeries.Application;
using MarketTimeSeries.Application.Contracts;
using MarketTimeSeries.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MarketTimeSeries.Api.Controllers;

[ApiController]
[Route("api/v1/time-series")]
public sealed class TimeSeriesController(TimeSeriesService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(TimeSeriesRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.ToDefinition(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error);
        }

        SetETag(result.Value);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, TimeSeriesResponse.From(result.Value));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToActionResult(result.Error);
        }

        SetETag(result.Value);
        return Ok(TimeSeriesResponse.From(result.Value));
    }

    // A strong ETag is a quoted string; the version is all a client needs to send back in If-Match.
    private void SetETag(TimeSeries series) => Response.Headers.ETag = $"\"{series.Version}\"";
}
