using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1744, #1745 (ADR 0142 D8) — the box feeds each login provider's client in from <c>deploy/.env</c>, and the
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
    public void ProviderClient_DefaultsToEmpty_WhenTheBoxSetsNothing(string key, string value) =>
        ComposeLines.Where(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal)).ShouldHaveSingleItem()
            .Trim().ShouldBe($"{key} {value}");

    [Theory]
    [InlineData("Google")]
    [InlineData("GitHub")]
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

        variables.Count.ShouldBe(4);
        variables.ShouldAllBe(name => offered.Any(l => l.StartsWith($"#{name}=", StringComparison.Ordinal)));
    }

    private static readonly Regex ComposeVariable = new(@"\$\{(?<name>AUTH_OAUTH_[A-Z_]+):-\}", RegexOptions.CultureInvariant);
}
