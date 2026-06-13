namespace Jobbliggaren.Domain.Common;

public class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(true, DomainError.None) => _value = value;
    internal Result(DomainError error) : base(false, error) => _value = default;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of failed result.");
}
