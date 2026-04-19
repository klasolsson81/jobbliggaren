namespace JobbPilot.Application.Common.Exceptions;

public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("En eller flera valideringsfel inträffade.")
    {
        Errors = errors.AsReadOnly();
    }
}
