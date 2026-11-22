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
        var acceptedFields = BodyFields(context);

        return ValidationProblem(
            context.HttpContext,
            context.ModelState.SelectMany(entry => entry.Value!.Errors.Select(modelError =>
                (entry.Key, ErrorMessage(ToFieldName(entry.Key), modelError, acceptedFields)))));
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

    // "Start" (domain) and "$.start" (JSON reader) both become "start", the name the client sent.
    private static string ToFieldName(string key) =>
        JsonNamingPolicy.CamelCase.ConvertName(key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key);

    // The JSON reader's own messages name .NET types, so they are hidden; this says what went wrong instead.
    private static string ErrorMessage(string field, ModelError error, IReadOnlyList<string> acceptedFields)
    {
        if (!string.IsNullOrEmpty(error.ErrorMessage))
        {
            return error.ErrorMessage;
        }

        var rootField = field.Split('.', '[')[0]; // "values[2]" belongs to "values"
        return acceptedFields.Count > 0 && rootField.Length > 0 && !acceptedFields.Contains(rootField)
            ? $"Unknown field. A request accepts {string.Join(", ", acceptedFields)}."
            : "The value is not valid.";
    }

    // The JSON names of the request body's properties, read from the type itself so the list never goes stale.
    private static List<string> BodyFields(ActionContext context) =>
    [
        .. context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .SelectMany(parameter => parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)),
    ];
}
