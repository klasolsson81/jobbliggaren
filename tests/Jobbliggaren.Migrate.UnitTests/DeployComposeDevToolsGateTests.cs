using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins the interpolation DEFAULT that feeds the throwaway dev-tooling flag on the box.
///
/// <para>
/// <c>DevToolsOptions.EnableResetMyData</c> defaults to <c>false</c> in code, and the handler
/// refuses independently of the map gate. Neither of them sees the value the box actually
/// feeds in: an interpolation default in <c>deploy/docker-compose.yml</c>. Flip
/// <c>${DEV_TOOLS_RESET_ENABLED:-false}</c> to <c>:-true</c> and nothing else goes red, while a
/// DESTRUCTIVE owner-scoped reset becomes reachable on a box with real test users on it and a
/// button appears on <c>/oversikt</c> for every logged-in account.
/// </para>
///
/// <para>
/// Both services are pinned, and by the SAME variable deliberately: the two halves are set
/// together or not at all. With only the API half on, the endpoint exists and nothing renders;
/// with only the web half on, the button renders and every press is refused. A rename that
/// leaves one service behind is the defect this catches.
/// </para>
///
/// <para>
/// <b>REMOVE BEFORE LAUNCH</b> with everything it pins — see
/// <c>docs/runbooks/release-checklist.md</c> 2.7, which names this file.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeDevToolsGateTests
{
    private static string ComposeText =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"));

    private static string LineContaining(string key) =>
        ComposeText.Split('\n').SingleOrDefault(l => l.Contains(key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"deploy/docker-compose.yml has no single line containing '{key}'. If the file was " +
            "restructured, this pin must be rewritten rather than deleted.");

    // The web key is matched WITH its value prefix on purpose. Bare
    // "DEV_TOOLS_RESET_ENABLED:" also occurs inside the api line's interpolation
    // (${DEV_TOOLS_RESET_ENABLED:-false}), so it selects two lines and the pin throws
    // instead of asserting.
    [Theory]
    [InlineData("DevTools__EnableResetMyData:")]
    [InlineData("DEV_TOOLS_RESET_ENABLED: $")]
    public void DevToolsResetFlag_DefaultsToFalse_WhenTheBoxSetsNothing(string key)
    {
        // The whole line, so the assertion fails on a changed variable name as well as on a
        // changed default — a rename that leaves the .env template behind is the same defect as
        // an enabled default, just slower to find.
        LineContaining(key).Trim()
            .ShouldBe($"{key.TrimEnd(' ', '$')} ${{DEV_TOOLS_RESET_ENABLED:-false}}");
    }

    [Fact]
    public void DevToolsResetFlag_IsWiredIntoBothServices_SoTheTwoHalvesCannotDrift()
    {
        // Not a restatement of the theory above: that one pins each line's VALUE, this one pins
        // that there are exactly two of them. A slot deleted from one service leaves the other
        // theory case green.
        var lines = ComposeText.Split('\n')
            .Where(l => l.Contains("DEV_TOOLS_RESET_ENABLED", StringComparison.Ordinal)
                        && !l.TrimStart().StartsWith('#'))
            .ToList();

        lines.Count.ShouldBe(2);
    }
}
