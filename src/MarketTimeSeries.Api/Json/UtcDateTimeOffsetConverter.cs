using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketTimeSeries.Api.Json;

/// <summary>
/// Reads timestamps only when they carry an explicit offset (a timestamp without one would silently be read
/// as server local time), and always writes them in UTC with a "Z", for example 2026-11-27T00:00:00Z.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.GetDateTime().Kind == DateTimeKind.Unspecified)
        {
            throw new JsonException("Timestamps must include an offset, for example 2026-11-27T00:00:00Z.");
        }

        return reader.GetDateTimeOffset();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime);
}
