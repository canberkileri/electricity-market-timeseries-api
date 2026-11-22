using MarketTimeSeries.Application;
using Microsoft.Extensions.DependencyInjection;

namespace MarketTimeSeries.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers storage. Switching to a database means changing this one registration.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Singleton: the in-memory store must live as long as the application.
        services.AddSingleton<ITimeSeriesRepository, InMemoryTimeSeriesRepository>();
        return services;
    }
}
