using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1744 (ADR 0142 D8) — the box feeds the Google client in from <c>deploy/.env</c>, and the defaults it feeds when
/// the file says nothing decide whether a provider exists. Both must default EMPTY: a blank client id registers no
/// provider, and a <c>:?</c> would refuse every hourly reconcile on a box without keys. And both must reach the api
/// alone, because the Worker composes no provider.
/// </summary>
public class DeployComposeGoogleLoginTests
{
    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml")).Split('\n');

    [Theory]
    [InlineData("Auth__OAuth__Google__ClientId:", "${AUTH_OAUTH_GOOGLE_CLIENT_ID:-}")]
    [InlineData("Auth__OAuth__Google__ClientSecret_FILE:", "${AUTH_OAUTH_GOOGLE_CLIENT_SECRET_FILE:-}")]
    public void GoogleClient_DefaultsToEmpty_WhenTheBoxSetsNothing(string key, string value) =>
        ComposeLines.Where(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal)).ShouldHaveSingleItem()
            .Trim().ShouldBe($"{key} {value}");

    [Fact]
    public void GoogleClient_ReachesTheApiServiceAlone()
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
            .Where(l => l.line.Contains("Auth__OAuth__Google__", StringComparison.Ordinal)
                        && !l.line.TrimStart().StartsWith('#'))
            .ToList();

        carriers.Count.ShouldBe(2);
        carriers.ShouldAllBe(l => l.index > api && l.index < next);
    }
}
