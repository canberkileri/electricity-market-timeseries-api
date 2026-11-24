using System.Reflection;
using System.Text.Json;
using MarketTimeSeries.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MarketTimeSeries.Api.Http;

/// <summary>The one place where expected failures become HTTP responses (RFC 9457 Problem Details).</summary>
internal static class ErrorResponses
{
    public static ActionResult ToActionResult(this ControllerBase controller, Error error) => error.Type switch
    {
        ErrorType.Validation => ValidationProblem(
            controller.HttpContext,
            error.ValidationErrors.Select(validationError => (validationError.Field, validationError.Message))),
        ErrorType.NotFound => controller.Problem(error.Message, statusCode: StatusCodes.Status404NotFound, title: "Time series not found"),
        ErrorType.Conflict => controller.Problem(error.Message, statusCode: StatusCodes.Status409Conflict, title: "Overlapping time series"),
        ErrorType.PreconditionFailed => controller.Problem(error.Message, statusCode: StatusCodes.Status412PreconditionFailed, title: "Version mismatch"),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unknown error type."),
    };

    /// <summary>
    /// Replaces the default 400 for invalid requests (missing fields, unreadable JSON), so those errors use the
    /// same field names as domain errors.
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var bodyFields = BodyFields(context);

        return ValidationProblem(
            context.HttpContext,
            context.ModelState.SelectMany(entry => entry.Value!.Errors.Select(modelError =>
                (entry.Key, ErrorMessage(ToFieldName(entry.Key), modelError, bodyFields)))));
    }

    private static BadRequestObjectResult ValidationProblem(HttpContext httpContext, IEnumerable<(string Field, string Message)> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, message) in errors)
        {
            modelState.AddModelError(ToFieldName(field), message);
        }

        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(httpContext, modelState, StatusCodes.Status400BadRequest);

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>"Start" (domain) and "$.start" (JSON reader) both become "start", the name the client sent.</summary>
    private static string ToFieldName(string key) =>
        JsonNamingPolicy.CamelCase.ConvertName(key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key);

    /// <summary>
    /// The JSON reader's own messages name .NET types, so they are hidden; this says what went wrong instead.
    /// </summary>
    private static string ErrorMessage(string field, ModelError error, IReadOnlyList<(string Name, Type Type)> bodyFields)
    {
        if (!string.IsNullOrEmpty(error.ErrorMessage))
        {
            return error.ErrorMessage;
        }

        // "values[2]" belongs to "values": the field name is everything before the first "." or "[".
        var end = field.IndexOfAny(['.', '[']);
        var rootField = end < 0 ? field : field[..end];
        if (bodyFields.Count == 0 || rootField.Length == 0)
        {
            return "The value is not valid.";
        }

        var match = bodyFields.FirstOrDefault(bodyField => bodyField.Name == rootField);
        return match.Type is null
            ? $"Unknown field. A request accepts {string.Join(", ", bodyFields.Select(bodyField => bodyField.Name))}."
            : ExpectedValue(match.Type);
    }

    /// <summary>What a field of this type expects, so a client can fix the request without reading the docs.</summary>
    private static string ExpectedValue(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;

        if (valueType.IsEnum)
        {
            return $"Must be one of {string.Join(", ", Enum.GetNames(valueType))}.";
        }

        if (valueType == typeof(DateTimeOffset))
        {
            return "Must be a UTC timestamp, for example 2026-11-27T00:00:00Z.";
        }

        return valueType == typeof(IReadOnlyList<decimal>)
            ? "Must be a list of numbers, one per period."
            : "The value is not valid.";
    }

    /// <summary>
    /// The JSON names and types of the request body's properties, read from the type itself so they never go stale.
    /// </summary>
    private static List<(string Name, Type Type)> BodyFields(ActionContext context) =>
    [
        .. context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .SelectMany(parameter => parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => (JsonNamingPolicy.CamelCase.ConvertName(property.Name), property.PropertyType)),
    ];
}
