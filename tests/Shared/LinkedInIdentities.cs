using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// LinkedIn identities as the production adapter reads them (#1746): a documented userinfo shape
/// (<see cref="LinkedInUserInfoShapes"/>) through <see cref="LinkedInIdentityProvider"/> over
/// <see cref="ScriptedLinkedIn"/>. A test that needs a LinkedIn identity or an <see cref="ExternalLoginProof"/> takes one
/// from here, never from a hand-built <see cref="VerifiedEmail"/>: only the adapter's parse may decide that an address
/// qualifies (AGENTS.md §5 <c>Tests:</c>).
/// </summary>
internal static class LinkedInIdentities
{
    public const string ClientId = "li-scripted-client-id";

    // Not shaped like a real secret. gitleaks:allow
    public const string ClientSecret = "test-linkedin-client-secret"; // gitleaks:allow

    /// <summary>The closed result the adapter gives for this shape, whatever it is.</summary>
    public static async Task<ExternalExchange> ExchangeAsync(string userInfoJson)
    {
        using var linkedin = new ScriptedLinkedIn(ClientId, ClientSecret);
        const string code = "scripted-identity-code";
        var verifier = PkceVerifier.Generate();
        linkedin.Expect(code, userInfoJson);

        var provider = new LinkedInIdentityProvider(
            new NamedClientFactory(LinkedInIdentityProvider.HttpClientName, linkedin),
            Options.Create(new LinkedInOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }),
            new ExternalLoginCallbacks(new Uri("https://jobbliggaren.example")),
            NullLogger<LinkedInIdentityProvider>.Instance);

        return await provider.ExchangeAsync(AuthorizationCode.FromRaw(code), verifier, CancellationToken.None);
    }

    /// <summary>An identified LinkedIn login; throws when the adapter refuses the shape, so a test cannot fake one.</summary>
    public static async Task<ExternalIdentity> ReadAsync(string userInfoJson) =>
        await ExchangeAsync(userInfoJson) is ExternalExchange.Identified { Identity: var identity }
            ? identity
            : throw new InvalidOperationException("The adapter refused a documented shape; the fixture is wrong.");

    /// <summary>The proof a LinkedIn login carries into the outcome function.</summary>
    public static async Task<ExternalLoginProof> ProofAsync(string userInfoJson)
    {
        var identity = await ReadAsync(userInfoJson);
        return new ExternalLoginProof(identity.Email, identity.Provider, identity.Subject);
    }
}
