using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1744 — joins what the web re-types about external logins to the backend's own values: the provider keys a route
/// segment may carry, how long the state cookie lives, where each provider's authorization request may point, and the
/// callback path the provider consoles register, which must be a route the web serves.
///
/// <para>
/// <b>Direction</b> is <see cref="AuthErrorCodeWireContractTests"/>'s: a backend change runs <c>dotnet test</c>. The
/// web's values are read out of the shipped source files, the backend's out of the real types.
/// </para>
/// </summary>
public class ExternalLoginMirrorWireContractTests
{
    private const string ExternalLoginModule = "web/jobbliggaren-web/src/lib/auth/external-login.ts";
    private const string SecurityHeadersModule = "web/jobbliggaren-web/src/lib/security/security-headers.ts";
    private const string AppRoot = "web/jobbliggaren-web/src/app";

    [Fact]
    public void The_web_knows_exactly_the_backends_provider_keys()
    {
        var keys = Regex.Matches(Capture(ExternalLoginModule, @"\bexport\s+const\s+EXTERNAL_PROVIDER_KEYS\s*=\s*\[([^\]]*)\]"), "\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value);

        keys.ShouldBe(ExternalProviderKey.Known.Select(key => key.Value), Hint(ExternalLoginModule));
    }

    [Fact]
    public void The_state_cookie_lives_as_long_as_the_flow() =>
        int.Parse(
                Capture(ExternalLoginModule, @"\bexport\s+const\s+OAUTH_STATE_MAX_AGE_SECONDS\s*=\s*(\d+)\s*\*\s*60\s*;"),
                CultureInfo.InvariantCulture)
            .ShouldBe((int)ExternalLoginPolicy.StateTtl.TotalMinutes, Hint(ExternalLoginModule));

    [Fact]
    public void The_start_accepts_only_the_adapters_own_authorization_endpoint() =>
        Capture(ExternalLoginModule, @"\bexport\s+const\s+AUTHORIZATION_ENDPOINTS[^=]*=\s*\{[^}]*\bgoogle\s*:\s*""([^""]+)""")
            .ShouldBe(GoogleIdentityProvider.AuthorizationEndpoint.AbsoluteUri, Hint(ExternalLoginModule));

    [Fact]
    public void The_callback_the_provider_returns_to_is_a_route_the_web_serves()
    {
        foreach (var key in ExternalProviderKey.Known)
        {
            var template = ExternalLoginCallbacks.PathFor(key).Replace($"/{key.Value}/", "/[provider]/", StringComparison.Ordinal);
            var route = Path.Combine(RepoRoot(), AppRoot, template.TrimStart('/'), "route.ts");

            File.Exists(route).ShouldBeTrue($"{ExternalLoginCallbacks.PathFor(key)} has no route file at {route}.");
            Capture(SecurityHeadersModule, @"\bexport\s+const\s+OAUTH_CALLBACK_ROUTE\s*=\s*""([^""]+)""")
                .ShouldBe(template.Replace("[provider]", ":provider", StringComparison.Ordinal), Hint(SecurityHeadersModule));
        }
    }

    private static string Hint(string module) =>
        $"{module} re-types this value from the backend. Change both sides in the same PR.";

    private static string Capture(string module, string pattern)
    {
        var group = Regex.Match(Read(module), pattern, RegexOptions.Singleline).Groups[1];
        return group.Success
            ? group.Value
            : throw new InvalidOperationException(
                $"Could not read '{pattern}' out of {module}. It was renamed or reshaped - re-make this join "
                + "deliberately, do not delete it.");
    }

    /// <summary>Comments go first: they name these values in prose. Line comments first, one names <c>/api/*</c>.</summary>
    private static string Read(string module)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), module.Replace('/', Path.DirectorySeparatorChar)));
        source = Regex.Replace(source, @"(?<!:)//[^\n]*", string.Empty);
        return Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/ExternalLoginMirrorWireContractTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
