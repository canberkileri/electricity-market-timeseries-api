namespace MarketTimeSeries.Application.Results;

/// <summary>
/// The outcome of a use case: a value or an <see cref="Results.Error"/>. The implicit conversions let a
/// use case simply <c>return series;</c> or <c>return Error.NotFound(...);</c>.
/// </summary>
public sealed class OperationResult<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private OperationResult(T? value, Error? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value. Check IsSuccess first.");

    public Error Error => _error
        ?? throw new InvalidOperationException("A successful result has no error. Check IsSuccess first.");

    public static implicit operator OperationResult<T>(T value) => new(value, null);

    public static implicit operator OperationResult<T>(Error error) => new(default, error);
}
