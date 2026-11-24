using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MarketTimeSeries.IntegrationTests;

internal static class ApiTestHelpers
{
    public const string BasePath = "/api/v1/time-series";

    /// <summary>Tests share running APIs; giving each test its own days keeps their series from overlapping.</summary>
    private static int _nextDay;

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A fresh UTC midnight that no other test uses.</summary>
    public static DateTime UniqueDay() =>
        new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Interlocked.Add(ref _nextDay, 10));

    public static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    /// <summary>Asserts a 400 Problem Details response whose <c>errors</c> name exactly these fields, and returns them.</summary>
    public static async Task<JsonElement> AssertValidationProblemAsync(HttpResponseMessage response, params string[] expectedFields)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.Equal(
            expectedFields.Order(StringComparer.Ordinal),
            errors.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal));
        return errors;
    }
}
