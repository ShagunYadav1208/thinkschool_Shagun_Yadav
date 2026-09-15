namespace ParkFlow.BuildingBlocks.Application;

/// <summary>
/// What kind of failure a <see cref="Result"/> carries, so a controller can map it to the right
/// HTTP status instead of collapsing every failure into 400. Before this (see ADR-001,
/// day-28/piece1), "reservation not found" (404) and "not yours" (403) were indistinguishable from
/// a plain validation failure — this type only fixes the shape; nothing yet constructs a
/// <see cref="Forbidden"/> result or changes what a controller does with one (that's Day 30).
/// </summary>
public enum ResultErrorKind
{
    Validation,
    NotFound,
    Forbidden,
}

/// <summary>
/// Outcome of an application-layer use case, without throwing exceptions for expected failures
/// (validation, business-rule violations). Reserve exceptions for the unexpected.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public ResultErrorKind? ErrorKind { get; }

    protected Result(bool isSuccess, string? error, ResultErrorKind? errorKind)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorKind = errorKind;
    }

    public static Result Success() => new(true, null, null);

    // Defaults to Validation so every existing call site (a bare `Result.Failure("...")`) keeps
    // compiling and behaving exactly as before; only call sites that need 404/403 pass errorKind explicitly.
    public static Result Failure(string error, ResultErrorKind errorKind = ResultErrorKind.Validation) => new(false, error, errorKind);
    public static Result NotFound(string error) => new(false, error, ResultErrorKind.NotFound);
    public static Result Forbidden(string error) => new(false, error, ResultErrorKind.Forbidden);

    public static Result<T> Success<T>(T value) => new(value, true, null, null);
    public static Result<T> Failure<T>(string error, ResultErrorKind errorKind = ResultErrorKind.Validation) => new(default, false, error, errorKind);
    public static Result<T> NotFound<T>(string error) => new(default, false, error, ResultErrorKind.NotFound);
    public static Result<T> Forbidden<T>(string error) => new(default, false, error, ResultErrorKind.Forbidden);
}

public class Result<T> : Result
{
    public T? Value { get; }

    internal Result(T? value, bool isSuccess, string? error, ResultErrorKind? errorKind) : base(isSuccess, error, errorKind)
    {
        Value = value;
    }
}
