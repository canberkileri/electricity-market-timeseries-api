using MarketTimeSeries.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

await app.RunAsync();
