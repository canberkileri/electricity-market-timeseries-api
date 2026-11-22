using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketTimeSeries.Api.Http;

internal static class JsonSetup
{
    /// <summary>
    /// The JSON rules of the API. Applied to controllers and to the OpenAPI document,
    /// so the document describes exactly what the API reads and writes.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        // Enums travel as their names ("SE3", "PT15M"); numbers such as 5 are rejected.
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new UtcDateTimeOffsetConverter());
    }
}
