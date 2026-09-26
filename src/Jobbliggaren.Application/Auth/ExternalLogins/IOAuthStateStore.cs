namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// What a started flow needs at its callback (ADR 0142 D8): the provider it was started for, the PKCE verifier and
/// the post-login path. <see cref="Next"/> is an echo, never an input.
/// </summary>
public sealed record OAuthFlow(ExternalProviderKey Provider, PkceVerifier Verifier, string? Next);

/// <summary>
/// The started flows, on the non-persisted Redis (ADR 0142 D1): one record per state, alive for
/// <see cref="ExternalLoginPolicy.StateTtl"/>, taken exactly once. The provider binding is asserted inside
/// <see cref="TakeAsync"/>, so no handler compares it.
/// </summary>
public interface IOAuthStateStore
{
    Task<OAuthState> PutAsync(OAuthFlow flow, CancellationToken ct);

    /// <summary>
    /// Takes the flow out of the store — a second take finds nothing — and returns it, or <c>null</c> for an unknown,
    /// expired, already used or unreadable state, and for one started for another provider, whose record is
    /// consumed all the same.
    /// </summary>
    Task<OAuthFlow?> TakeAsync(OAuthState state, ExternalProviderKey expected, CancellationToken ct);
}

