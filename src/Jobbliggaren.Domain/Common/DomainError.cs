namespace Jobbliggaren.Domain.Common;

public sealed record DomainError(string Code, string Message)
{
    /// <summary>
    /// The semantic kind, used by the API layer's central mapper to pick the HTTP status
    /// (TD-63 kind-union; #203). Defaults to <see cref="ErrorKind.Validation"/> (the 400 floor)
    /// so any direct construction is safe; the factories below set the precise kind.
    /// </summary>
    public ErrorKind Kind { get; init; } = ErrorKind.Validation;

    public static readonly DomainError None = new(string.Empty, string.Empty);

    /// <summary>Entity-not-found (→404). Builds the conventional <c>"{entity}.NotFound"</c> code.</summary>
    public static DomainError NotFound(string entity, object id) =>
        new($"{entity}.NotFound", $"{entity} med id {id} hittades inte.") { Kind = ErrorKind.NotFound };

    /// <summary>
    /// Not-found with an EXPLICIT code + message (→404) — for not-found cases whose code does not
    /// follow the <c>"{entity}.NotFound"</c> shape or whose message is bespoke (e.g. a token lookup
    /// that found nothing). Distinct from <see cref="Validation"/> only in <see cref="Kind"/>.
    /// </summary>
    public static DomainError NotFound(string code, string message) =>
        new(code, message) { Kind = ErrorKind.NotFound };

    public static DomainError Validation(string code, string message) =>
        new(code, message) { Kind = ErrorKind.Validation };

    public static DomainError Conflict(string code, string message) =>
        new(code, message) { Kind = ErrorKind.Conflict };

    /// <summary>
    /// A resource that existed but is no longer actionable (→410) — e.g. an invitation that has
    /// expired, been revoked, or already been redeemed.
    /// </summary>
    public static DomainError Gone(string code, string message) =>
        new(code, message) { Kind = ErrorKind.Gone };
}
