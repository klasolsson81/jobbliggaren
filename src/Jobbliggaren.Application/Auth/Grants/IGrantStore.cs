namespace Jobbliggaren.Application.Auth.Grants;

/// <summary>
/// Short-lived, single-use grants (ADR 0142 D3): one port, the purpose inside the subject, the bindings
/// asserted inside <see cref="RedeemAsync"/> so no handler compares and none can forget. The adapter mints
/// the token, protects the payload and fixes the lifetime (<c>LoginChallengePolicy.GrantTtl</c>).
/// </summary>
public interface IGrantStore
{
    Task<GrantToken> IssueAsync(GrantSubject subject, CancellationToken ct);

    /// <summary>
    /// Takes the grant out of the store — a second redemption finds nothing — and returns its subject, or
    /// <c>null</c> for an unknown, expired, already used or unreadable grant, a different purpose and a
    /// different binding alike.
    /// </summary>
    Task<GrantSubject?> RedeemAsync(GrantToken token, GrantAssertion expected, CancellationToken ct);
}
