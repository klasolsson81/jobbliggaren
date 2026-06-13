namespace Jobbliggaren.Domain.Common;

public sealed record DomainError(string Code, string Message)
{
    public static readonly DomainError None = new(string.Empty, string.Empty);

    public static DomainError NotFound(string entity, object id) =>
        new($"{entity}.NotFound", $"{entity} med id {id} hittades inte.");

    public static DomainError Validation(string code, string message) =>
        new(code, message);

    public static DomainError Conflict(string code, string message) =>
        new(code, message);
}
