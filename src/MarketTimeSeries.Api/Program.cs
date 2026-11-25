using MarketTimeSeries.Api;
using MarketTimeSeries.Api.Errors;
using MarketTimeSeries.Api.Json;
using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // The largest valid request (35,136 values) is well under 1 MB; anything bigger is rejected before it is read,
    // so an oversized body cannot make the server parse megabytes of JSON.
    kestrel.Limits.MaxRequestBodySize = 1024 * 1024;

    // No "Server: Kestrel" header: naming the server only helps someone looking for its known weaknesses.
    kestrel.AddServerHeader = false;
});

// Outside Development, logs are JSON lines that a log collector can parse, and every line carries the request's
// TraceId (from the logging scope), so the traceId a client gets in Problem Details leads to its log lines.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole(json =>
    {
        json.IncludeScopes = true;
        json.UseUtcTimestamp = true;
        json.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    });
}

builder.Services
    .AddControllers(options =>
    {
        // Only explicit [Required] attributes count; nullable annotations alone do not make a field required.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        JsonSetup.Configure(options.JsonSerializerOptions);
        // Do not echo .NET type names from JSON errors back to clients.
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ErrorResponses.InvalidModelState);

// The OpenAPI document reads these general JSON options, so they get the same rules as the controllers.
builder.Services.ConfigureHttpJsonOptions(options => JsonSetup.Configure(options.SerializerOptions));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TimeSeriesService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Browsers must not guess a response's type and treat JSON as something else, such as script. Set as the response
// starts, so error responses keep it too: the exception handler clears the headers before writing its Problem Details.
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Task.CompletedTask;
    });
    await next(context);
});

// Every exception goes to GlobalExceptionHandler: the right status and log level per case, never internal details.
app.UseExceptionHandler();
// Empty error responses, such as 404 for an unknown route, also get a Problem Details body.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    try
    {
        await app.Services.EnsureStorageCreatedAsync();
    }
    catch (StorageUnavailableException exception)
    {
        // One clear line instead of a stack trace: the store is missing, not broken code.
        app.Logger.LogCritical("{Reason}", exception.Message);
        return 1;
    }
    await SeedData.AddAsync(app.Services);
}

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();
return 0;
