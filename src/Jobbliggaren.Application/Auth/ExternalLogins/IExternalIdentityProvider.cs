namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// One identity provider, hand-rolled over <c>HttpClient</c> (ADR 0142 D8, Variant B). The adapter builds the
/// <c>redirect_uri</c> itself from the site's one public base URL, so the authorization request and the token
/// request can never carry two different values. A provider without keys is not registered at all: the set of
/// registered implementations IS the providers list.
/// </summary>
public interface IExternalIdentityProvider
{
    ExternalProviderKey Key { get; }

    /// <summary>The provider's authorization URL for this flow: S256 only, the state echoed back by the provider.</summary>
    Uri BuildAuthorizeUrl(OAuthState state, PkceChallenge challenge);

    /// <summary>
    /// Exchanges the code at the provider and reads who the user is, as one closed result: identified, the address
    /// refused by the provider's rule, or failed. Never null; the adapter logs the cause of a refusal. The caller's
    /// cancellation propagates.
    /// </summary>
    Task<ExternalExchange> ExchangeAsync(AuthorizationCode code, PkceVerifier verifier, CancellationToken ct);
}

/// <summary>
/// Who a provider says the user is, and the address its rule admitted in the strength the provider can vouch for
/// (ADR 0142 D8, Amendment (16)): only an <see cref="ExternalAddress.Authoritative"/> address may link or register
/// on its own; an <see cref="ExternalAddress.Asserted"/> one only chooses where a code goes.
/// </summary>
public sealed record ExternalIdentity(ExternalProviderKey Provider, ExternalSubject Subject, ExternalAddress Address);

/// <summary>
/// An OAuth proof on its way to the outcome function (ADR 0142 D8, security-auditor m-3): the address stays a
/// <see cref="VerifiedEmail"/> until the account is resolved, so no caller can hand the outcome an unverified string.
/// </summary>
public sealed record ExternalLoginProof(VerifiedEmail Email, ExternalProviderKey Provider, ExternalSubject Subject);

/// <summary>
/// A provider login whose address the provider asserts but is not the mailbox of (ADR 0142 Amendment (16), #1745):
/// it reaches a session only through a link a code bound, and otherwise only the address a code is sent to.
/// </summary>
public sealed record AssertedLoginProof(AssertedEmail Address, ExternalProviderKey Provider, ExternalSubject Subject);
