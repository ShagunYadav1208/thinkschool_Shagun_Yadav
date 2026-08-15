namespace QuotesApi.Models;

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, IReadOnlyList<QuoteError> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<QuoteError> Errors { get; }

    public static Result<T> Success(T value) => new(true, value, []);

    public static Result<T> Failure(IReadOnlyList<QuoteError> errors) => new(false, default, errors);
}
