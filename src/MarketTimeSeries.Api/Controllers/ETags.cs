using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace MarketTimeSeries.Api.Controllers;

/// <summary>The series version travels as a strong ETag: version 3 is <c>"3"</c>.</summary>
internal static class ETags
{
    public static string Format(int version) => $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// Reads an If-Match header. Missing or <c>*</c> means "any version" (<paramref name="version"/> is null).
    /// Returns false for anything that is not a single strong version ETag, such as <c>W/"3"</c> or <c>abc</c>.
    /// </summary>
    public static bool TryParseIfMatch(string? header, out int? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(header) || header.Trim() == "*")
        {
            return true;
        }

        if (EntityTagHeaderValue.TryParse(header, out var tag)
            && !tag.IsWeak
            && int.TryParse(tag.Tag.Value!.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            version = parsed;
            return true;
        }

        return false;
    }
}
