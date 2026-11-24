using Microsoft.AspNetCore.Diagnostics;

namespace MarketTimeSeries.Api.Errors;

/// <summary>
/// The one place where exceptions become responses (business outcomes never get here: they are results, mapped in
/// <see cref="ErrorResponses"/>). Each case gets the right status and the right log level, so client mistakes and
/// cancelled requests do not look like server failures, and a real failure never reveals internal details.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Not an HTTP status: the convention (from NGINX) for "the client closed the request".</summary>
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            // Rejected by the server before our code ran, for example a body over the 1 MB limit: a client mistake.
            case BadHttpRequestException badRequest:
                logger.LogInformation("Rejected request with {StatusCode}: {Reason}", badRequest.StatusCode, badRequest.Message);
                return await WriteAsync(httpContext, badRequest.StatusCode, detail: badRequest.Message);

            // The client went away; nobody is listening for a response.
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                logger.LogInformation("Request cancelled by the client");
                httpContext.Response.StatusCode = ClientClosedRequest;
                return true;

            // A real failure: logged in full, answered with a generic 500 that reveals nothing internal.
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                return await WriteAsync(httpContext, StatusCodes.Status500InternalServerError, detail: null);
        }
    }

    private async ValueTask<bool> WriteAsync(HttpContext httpContext, int statusCode, string? detail)
    {
        httpContext.Response.StatusCode = statusCode;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = statusCode, Detail = detail },
        });
    }
}
