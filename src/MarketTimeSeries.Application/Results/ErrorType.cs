namespace MarketTimeSeries.Application.Results;

/// <summary>Expected ways a use case can fail. The Api maps each one to an HTTP status code in one place.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    PreconditionFailed,
}
