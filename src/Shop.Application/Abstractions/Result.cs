namespace Shop.Application.Abstractions;

/// <summary>Expected failure surfaced to API clients: stable code, message and optional offending field.</summary>
public sealed record AppError(string Code, string Message, string? Field = null);

/// <summary>Outcome of a request: either a value or one or more expected errors.</summary>
public sealed record Result<T>
{
    private Result(T? value, IReadOnlyList<AppError> errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }

    public IReadOnlyList<AppError> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public static Result<T> Success(T? value) => new(value, []);

    public static Result<T> Failure(params IReadOnlyList<AppError> errors) =>
        errors.Count == 0 ? throw new ArgumentException("At least one error is required.", nameof(errors)) : new(default, errors);
}
