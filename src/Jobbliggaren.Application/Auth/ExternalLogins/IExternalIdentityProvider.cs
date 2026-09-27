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
/// Who a provider says the user is, and the address the adapter's rule admitted (ADR 0142 D8): only an adapter decides
/// that an address qualifies, and only a <see cref="VerifiedEmail"/> may link or register.
/// </summary>
public sealed record ExternalIdentity(ExternalProviderKey Provider, ExternalSubject Subject, VerifiedEmail Email);

/// <summary>
/// An OAuth proof on its way to the outcome function (ADR 0142 D8, security-auditor m-3): the address stays a
/// <see cref="VerifiedEmail"/> until the account is resolved, so no caller can hand the outcome an unverified string.
/// </summary>
public sealed record ExternalLoginProof(VerifiedEmail Email, ExternalProviderKey Provider, ExternalSubject Subject);
