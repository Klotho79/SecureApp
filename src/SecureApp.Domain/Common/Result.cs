namespace SecureApp.Domain.Common;

/// <summary>
/// Railway-oriented result for operations where failure is an expected,
/// non-exceptional outcome (e.g. "wrong vault passphrase", "unsupported file
/// type") — reserving thrown exceptions for truly exceptional conditions.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A successful result cannot have an error message.");
        if (!isSuccess && string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException("A failed result requires a non-empty error message.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value, true, null);
    public static Result<T> Failure<T>(string error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    internal Result(T? value, bool isSuccess, string? error) : base(isSuccess, error) => _value = value;
}
