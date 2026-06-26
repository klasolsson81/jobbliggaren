namespace Jobbliggaren.Domain.Common;

/// <summary>
/// The semantic kind of a <see cref="DomainError"/> — the discriminator the API layer maps to an
/// HTTP status via ONE central mapper (Validation→400, NotFound→404, Conflict→409, Gone→410),
/// instead of per-endpoint string-code matching (TD-63 kind-union; #203 / TD-84). Adding a kind
/// here + a case in the mapper is the only place a new error→status rule is expressed (OCP).
/// <para>
/// This is the Result-side error contract (CLAUDE.md §3: expected failures → <c>Result</c>). The
/// parallel exception-side contract (<c>NotFoundException</c>→404, <c>DomainException</c>→400, …)
/// stays mapped by the API middleware; the two idioms coexist deliberately.
/// </para>
/// </summary>
public enum ErrorKind
{
    /// <summary>An expected precondition/input failure → HTTP 400.</summary>
    Validation,

    /// <summary>A target entity does not exist → HTTP 404.</summary>
    NotFound,

    /// <summary>The request conflicts with the current resource state → HTTP 409.</summary>
    Conflict,

    /// <summary>The resource existed but is no longer actionable (e.g. an expired/revoked/
    /// already-redeemed invitation) → HTTP 410.</summary>
    Gone,
}
