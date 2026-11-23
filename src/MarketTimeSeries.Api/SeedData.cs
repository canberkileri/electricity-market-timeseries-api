using MarketTimeSeries.Application;
using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Api;

/// <summary>
/// Sample series for the current UTC day, so the API can be tried right away. Development only. The series go
/// through <see cref="TimeSeriesService"/>, so they pass exactly the rules a real request does.
/// </summary>
internal static class SeedData
{
    public static async Task AddAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TimeSeriesService>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeedData));

        var today = new DateTimeOffset(timeProvider.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);

        foreach (var definition in Definitions(today))
        {
            var result = await service.CreateAsync(definition, cancellationToken);
            if (result is { IsSuccess: false, Error.Type: ErrorType.Conflict })
            {
                // A persistent store (SQL Server) still holds today's seed from an earlier start: nothing to do.
                logger.LogInformation(
                    "Seed series {Type} for {BiddingZone} is already present.",
                    definition.Type,
                    definition.BiddingZone);
            }
            else if (!result.IsSuccess)
            {
                logger.LogWarning(
                    "Seed series {Type} for {BiddingZone} was not created: {Reason}",
                    definition.Type,
                    definition.BiddingZone,
                    result.Error.Message);
            }
        }
    }

    private static IEnumerable<TimeSeriesDefinition> Definitions(DateTimeOffset today)
    {
        // Hourly hydro and nuclear production in SE3: about 9,000 MW at night, 11,500 MW in the afternoon.
        yield return new TimeSeriesDefinition(
            TimeSeriesType.Production, BiddingZone.SE3, null, MeasurementUnit.MW, Resolution.PT60M, today,
            DailyCurve(periods: 24, average: 10_250, amplitude: 1_250));

        // Quarter-hourly consumption in DK1, lowest at night.
        yield return new TimeSeriesDefinition(
            TimeSeriesType.Consumption, BiddingZone.DK1, null, MeasurementUnit.MW, Resolution.PT15M, today,
            DailyCurve(periods: 96, average: 2_700, amplitude: 800));

        // Flow on the DK1 to SE3 border, changing direction during the day: negative values flow from SE3 to DK1.
        yield return new TimeSeriesDefinition(
            TimeSeriesType.CrossBorderFlow, BiddingZone.DK1, BiddingZone.SE3, MeasurementUnit.MW, Resolution.PT15M, today,
            DailyCurve(periods: 96, average: 100, amplitude: 450));
    }

    // One smooth wave per day, lowest around 03:00 and highest around 15:00, rounded to 0.1 MW.
    private static decimal[] DailyCurve(int periods, double average, double amplitude) =>
        [.. Enumerable.Range(0, periods).Select(period =>
            Math.Round((decimal)(average + (amplitude * Math.Sin(((period / (double)periods) - 0.375) * 2 * Math.PI))), 1))];
}
