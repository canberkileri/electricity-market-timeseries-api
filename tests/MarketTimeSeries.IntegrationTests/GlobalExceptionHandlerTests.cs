using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MarketTimeSeries.IntegrationTests;

/// <summary>Calls the API's registered exception handler directly, for cases that are hard to provoke over HTTP.</summary>
public sealed class GlobalExceptionHandlerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private IExceptionHandler Handler => Assert.Single(factory.Services.GetServices<IExceptionHandler>());

    private HttpContext Context(CancellationToken requestAborted) => new DefaultHttpContext
    {
        RequestServices = factory.Services,
        RequestAborted = requestAborted,
        Response = { Body = new MemoryStream() },
    };

    /// <summary>A client that went away is not a server failure: no 500, and nothing is written.</summary>
    [Fact]
    public async Task TryHandleAsync_RequestCancelledByClient_Returns499WithoutBody()
    {
        var context = Context(new CancellationToken(canceled: true));

        var handled = await Handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    /// <summary>A cancellation the client did not cause is a real failure.</summary>
    [Fact]
    public async Task TryHandleAsync_CancellationWhileClientStillConnected_Returns500()
    {
        var context = Context(CancellationToken.None);

        var handled = await Handler.TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }
}
