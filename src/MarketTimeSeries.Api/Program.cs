using System.Text.Json.Serialization;
using MarketTimeSeries.Api.Http;
using MarketTimeSeries.Application;
using MarketTimeSeries.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options =>
    {
        // Only explicit [Required] attributes count; nullable annotations alone do not make a field required.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        // Enums travel as their names ("SE3", "PT15M"); numbers such as 5 are rejected.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
        // Do not echo .NET type names from JSON errors back to clients.
        options.AllowInputFormatterExceptionMessages = false;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ErrorResponses.InvalidModelState);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TimeSeriesService>();
builder.Services.AddInfrastructure();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Unexpected exceptions become a generic 500 Problem Details response and are logged server-side.
app.UseExceptionHandler();
// Empty error responses, such as 404 for an unknown route, also get a Problem Details body.
app.UseStatusCodePages();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();
