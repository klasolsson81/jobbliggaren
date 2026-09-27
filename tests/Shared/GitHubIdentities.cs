using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// GitHub identities as the production adapter reads them (#1745): a documented <c>/user</c> and <c>/user/emails</c>
/// shape (<see cref="GitHubApiShapes"/>) through <see cref="GitHubIdentityProvider"/> over <see cref="ScriptedGitHub"/>.
/// A test that needs a GitHub identity or an <see cref="ExternalLoginProof"/> takes one from here, never from a
/// hand-built <see cref="VerifiedEmail"/>: only the adapter's parse may decide that an address qualifies (AGENTS.md §5
/// <c>Tests:</c>).
/// </summary>
internal static class GitHubIdentities
{
    public const string ClientId = "Iv23-scripted-client-id";

    // Not shaped like a real secret. gitleaks:allow
    public const string ClientSecret = "test-github-client-secret"; // gitleaks:allow

    /// <summary>The closed result the adapter gives for this shape, whatever it is.</summary>
    public static async Task<ExternalExchange> ExchangeAsync(string userJson, string emailsJson)
    {
        using var github = new ScriptedGitHub(ClientId, ClientSecret);
        const string code = "scripted-identity-code";
        github.Expect(code, userJson, emailsJson);

        var provider = new GitHubIdentityProvider(
            new NamedClientFactory(GitHubIdentityProvider.HttpClientName, github),
            Options.Create(new GitHubOAuthOptions { ClientId = ClientId, ClientSecret = ClientSecret }),
            new ExternalLoginCallbacks(new Uri("https://jobbliggaren.example")),
            NullLogger<GitHubIdentityProvider>.Instance);

        return await provider.ExchangeAsync(
            AuthorizationCode.FromRaw(code), PkceVerifier.Generate(), CancellationToken.None);
    }

    /// <summary>An identified GitHub login; throws when the adapter refuses the shape, so a test cannot fake one.</summary>
    public static async Task<ExternalIdentity> ReadAsync(string userJson, string emailsJson) =>
        await ExchangeAsync(userJson, emailsJson) is ExternalExchange.Identified { Identity: var identity }
            ? identity
            : throw new InvalidOperationException("The adapter refused a documented shape; the fixture is wrong.");

    /// <summary>The proof a GitHub login carries into the outcome function.</summary>
    public static async Task<ExternalLoginProof> ProofAsync(string userJson, string emailsJson)
    {
        var identity = await ReadAsync(userJson, emailsJson);
        return new ExternalLoginProof(identity.Email, identity.Provider, identity.Subject);
    }
}
