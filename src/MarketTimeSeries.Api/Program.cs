using MarketTimeSeries.Api;
using MarketTimeSeries.Api.Http;
using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// The largest valid request (35,136 values) is well under 1 MB; anything bigger is rejected before it is read,
// so an oversized body cannot make the server parse megabytes of JSON.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);

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

// Every exception goes to GlobalExceptionHandler: the right status and log level per case, never internal details.
app.UseExceptionHandler();
// Empty error responses, such as 404 for an unknown route, also get a Problem Details body.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    await app.Services.EnsureStorageCreatedAsync();
    await SeedData.AddAsync(app.Services);
}

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();
