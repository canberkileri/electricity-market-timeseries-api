using MarketTimeSeries.Api;
using MarketTimeSeries.Api.Http;
using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TimeSeriesService>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

// Unexpected exceptions become a generic 500 Problem Details response and are logged server-side.
app.UseExceptionHandler();
// Empty error responses, such as 404 for an unknown route, also get a Problem Details body.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document at /openapi/v1.json and an interactive UI at /scalar.
    app.MapOpenApi();
    app.MapScalarApiReference();

    await app.Services.EnsureStorageCreatedAsync();
    await SeedData.AddAsync(app.Services);
}

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();
