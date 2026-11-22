namespace MarketTimeSeries.Domain;

/// <summary>Either a value or the list of rules that prevented creating it. Expected failures are returned, not thrown.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value, IReadOnlyList<ValidationError> errors)
    {
        _value = value;
        Errors = errors;
    }

    public IReadOnlyList<ValidationError> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    /// <summary>The value of a successful result. Reading it from a failed result is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value. Check IsSuccess first.");

    public static Result<T> Success(T value) => new(value, []);

    public static Result<T> Failure(IReadOnlyList<ValidationError> errors) => errors.Count > 0
        ? new(default!, errors)
        : throw new ArgumentException("A failure needs at least one error.", nameof(errors));
}
