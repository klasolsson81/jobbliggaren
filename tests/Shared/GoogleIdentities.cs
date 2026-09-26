using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Identities as the production Google adapter reads them (#1744): a documented userinfo shape
/// (<see cref="GoogleUserInfoShapes"/>) through <see cref="GoogleIdentityProvider"/> over <see cref="ScriptedGoogle"/>.
/// A test that needs an <see cref="ExternalIdentity"/> or an <see cref="ExternalLoginProof"/> takes one from here,
/// never from a hand-built <see cref="VerifiedEmail"/>: the authority rule is the adapter's, and only its parse may
/// decide that an address is verified (AGENTS.md §5 <c>Tests:</c>; test-writer, 6a form round, Major 1).
/// </summary>
internal static class GoogleIdentities
{
    public static async Task<ExternalIdentity> ReadAsync(string userInfoJson)
    {
        using var google = new ScriptedGoogle();
        const string code = "4/0AVGzR1scripted-identity";
        google.Expect(code, userInfoJson);

        var provider = new GoogleIdentityProvider(
            new SingleClientFactory(google),
            Options.Create(new GoogleOAuthOptions { ClientId = "test-client", ClientSecret = "test-secret" }),
            new ExternalLoginCallbacks(new Uri("https://jobbliggaren.example")),
            NullLogger<GoogleIdentityProvider>.Instance);

        return await provider.ExchangeAsync(AuthorizationCode.FromRaw(code), PkceVerifier.Generate(), CancellationToken.None)
            ?? throw new InvalidOperationException("The adapter refused a documented shape; the fixture is wrong.");
    }

    /// <summary>A proof from a verified identity; throws when the shape does not verify, so a test cannot fake one.</summary>
    public static async Task<ExternalLoginProof> ProofAsync(string userInfoJson)
    {
        var identity = await ReadAsync(userInfoJson);
        return new ExternalLoginProof(
            identity.Email ?? throw new InvalidOperationException("This shape does not verify its address."),
            identity.Provider,
            identity.Subject);
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
