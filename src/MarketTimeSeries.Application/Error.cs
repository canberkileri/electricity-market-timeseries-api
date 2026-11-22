using MarketTimeSeries.Domain;

namespace MarketTimeSeries.Application;

/// <summary>Why a use case failed. <see cref="ValidationErrors"/> is only filled for <see cref="ErrorType.Validation"/>.</summary>
public sealed record Error(ErrorType Type, string Message, IReadOnlyList<ValidationError> ValidationErrors)
{
    public static Error Validation(IReadOnlyList<ValidationError> errors) =>
        new(ErrorType.Validation, "One or more validation errors occurred.", errors);

    public static Error NotFound(string message) => new(ErrorType.NotFound, message, []);

    public static Error Conflict(string message) => new(ErrorType.Conflict, message, []);

    public static Error PreconditionFailed(string message) => new(ErrorType.PreconditionFailed, message, []);
}
