using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1744, #1745, #1746 (ADR 0142 D8) — the box feeds each login provider's client in from <c>deploy/.env</c>, and the
/// defaults it feeds when the file says nothing decide whether a provider exists. Every line must default EMPTY: a
/// blank client id registers no provider, and a <c>:?</c> would refuse every hourly reconcile on a box without keys.
/// And every line must reach the api alone, because the Worker composes no provider.
/// </summary>
public class DeployComposeExternalLoginTests
{
    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml")).Split('\n');

    [Theory]
    [InlineData("Auth__OAuth__Google__ClientId:", "${AUTH_OAUTH_GOOGLE_CLIENT_ID:-}")]
    [InlineData("Auth__OAuth__Google__ClientSecret_FILE:", "${AUTH_OAUTH_GOOGLE_CLIENT_SECRET_FILE:-}")]
    [InlineData("Auth__OAuth__GitHub__ClientId:", "${AUTH_OAUTH_GITHUB_CLIENT_ID:-}")]
    [InlineData("Auth__OAuth__GitHub__ClientSecret_FILE:", "${AUTH_OAUTH_GITHUB_CLIENT_SECRET_FILE:-}")]
    [InlineData("Auth__OAuth__LinkedIn__ClientId:", "${AUTH_OAUTH_LINKEDIN_CLIENT_ID:-}")]
    [InlineData("Auth__OAuth__LinkedIn__ClientSecret_FILE:", "${AUTH_OAUTH_LINKEDIN_CLIENT_SECRET_FILE:-}")]
    public void ProviderClient_DefaultsToEmpty_WhenTheBoxSetsNothing(string key, string value) =>
        ComposeLines.Where(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal)).ShouldHaveSingleItem()
            .Trim().ShouldBe($"{key} {value}");

    [Theory]
    [InlineData("Google")]
    [InlineData("GitHub")]
    [InlineData("LinkedIn")]
    public void ProviderClient_ReachesTheApiServiceAlone(string provider)
    {
        var lines = ComposeLines;
        var api = Array.FindIndex(lines, l => l.TrimEnd() == "  api:");
        var next = Array.FindIndex(lines, api + 1, l => l.Length > 2 && l.StartsWith("  ", StringComparison.Ordinal)
                                                         && !l.StartsWith("   ", StringComparison.Ordinal)
                                                         && !l.TrimStart().StartsWith('#')
                                                         && l.TrimEnd().EndsWith(':'));
        api.ShouldBeGreaterThan(-1, "no api service header");
        next.ShouldBeGreaterThan(api, "no service header after api");

        var carriers = lines.Select((line, index) => (line, index))
            .Where(l => l.line.Contains($"Auth__OAuth__{provider}__", StringComparison.Ordinal)
                        && !l.line.TrimStart().StartsWith('#'))
            .ToList();

        carriers.Count.ShouldBe(2);
        carriers.ShouldAllBe(l => l.index > api && l.index < next);
    }

    [Fact]
    public void EveryProviderVariable_IsOfferedInTheEnvExample()
    {
        // #1745 (test-writer Major 5): a variable compose reads and deploy/.env.example never names is one a box
        // cannot set by following the file, and it fails silently as an empty providers list.
        var variables = ComposeLines
            .Where(l => l.TrimStart().StartsWith("Auth__OAuth__", StringComparison.Ordinal))
            .Select(l => ComposeVariable.Match(l).Groups["name"].Value)
            .ToList();
        var offered = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", ".env.example")).Split('\n')
            .Select(l => l.Trim())
            .ToList();

        variables.Count.ShouldBe(6);
        variables.ShouldAllBe(name => offered.Any(l => l.StartsWith($"#{name}=", StringComparison.Ordinal)));
    }

    [Fact]
    public void EverySecretComposeReads_IsInjectedByTheScript_AndCoveredByItsTest()
    {
        // #1746 (test-writer Minor 5): compose, OAUTH_CLIENT_SECRETS and the script test's cases are three hand-kept
        // lists. A provider missing from the script is a false green from --check, the box's one alarm surface, and a
        // secret with no scripted way in, since the injection iterates the same list.
        var composed = ComposeLines
            .Select(line => ComposeSecret.Match(line))
            .Where(match => match.Success)
            .Select(match => (Provider: match.Groups["provider"].Value, Prefix: match.Groups["prefix"].Value))
            .ToList();
        var script = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "deploy", "systemd", "jobbliggaren-inject-secrets.sh"));
        var scriptTest = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "deploy", "systemd", "jobbliggaren-inject-secrets.test.sh"));

        composed.Count.ShouldBe(3);
        foreach (var (provider, prefix) in composed)
        {
            script.ShouldContain($"\"Auth__OAuth__{provider}__ClientSecret|{prefix}\"");
            scriptTest.ShouldContain($"oauth_client_cases \"Auth__OAuth__{provider}__ClientSecret\" \"{prefix}\"");
        }
    }

    private static readonly Regex ComposeVariable = new(@"\$\{(?<name>AUTH_OAUTH_[A-Z_]+):-\}", RegexOptions.CultureInvariant);

    private static readonly Regex ComposeSecret = new(
        @"^\s*Auth__OAuth__(?<provider>[A-Za-z]+)__ClientSecret_FILE: \$\{AUTH_OAUTH_(?<prefix>[A-Z]+)_CLIENT_SECRET_FILE:-\}",
        RegexOptions.CultureInvariant);
}
