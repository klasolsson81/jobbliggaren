namespace Jobbliggaren.Domain.Common;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public DomainError Error { get; }

    protected Result(bool isSuccess, DomainError error)
    {
        if (isSuccess && error != DomainError.None)
            throw new InvalidOperationException("Success result cannot have error.");
        if (!isSuccess && error == DomainError.None)
            throw new InvalidOperationException("Failure result must have error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, DomainError.None);
    public static Result Failure(DomainError error) => new(false, error);
    public static Result<T> Success<T>(T value) => new Result<T>(value);
    public static Result<T> Failure<T>(DomainError error) => new Result<T>(error);
}
