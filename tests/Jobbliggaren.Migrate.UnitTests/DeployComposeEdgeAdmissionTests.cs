using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins the value the box feeds the edge's admission mode when <c>deploy/.env</c> says nothing
/// (#1768, ADR 0154).
///
/// <para>
/// What this pin sees that nothing else does: the edge test (<c>deploy/caddy/edge-modes.test.sh</c>)
/// proves what Caddy does with each value, but never which value compose hands it. Compose always
/// sets the variable, so its default is the one that runs. Flip <c>:-basic_auth</c> to <c>:-open</c>
/// and the next reconcile publishes the site with no operator having chosen to.
/// </para>
///
/// <para>
/// Forward-scanning from the service key, as in <see cref="DeployComposeIngestGateTests"/>.
/// </para>
/// </summary>
public class DeployComposeEdgeAdmissionTests
{
    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"))
            .Split('\n');

    private static string EnvExample =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", ".env.example"));

    private static bool IsTwoSpaceKey(string line) =>
        line.Length > 2
        && line.StartsWith("  ", StringComparison.Ordinal)
        && line[2] != ' '
        && line.TrimEnd().EndsWith(':');

    private static string[] CaddyBlock()
    {
        var lines = ComposeLines;
        var start = Array.FindIndex(lines, l => l.StartsWith("  caddy:", StringComparison.Ordinal));
        start.ShouldBeGreaterThan(-1, "the compose file no longer declares a `caddy` service");
        var end = Array.FindIndex(lines, start + 1, IsTwoSpaceKey);
        if (end < 0) end = lines.Length;
        return lines[(start + 1)..end];
    }

    [Theory]
    [InlineData("SITE_ADMISSION", "basic_auth")]
    [InlineData("SITE_ALIASES", "none")]
    public void EdgeMode_DefaultsToTheGatedEdge_WhenTheBoxSetsNothing(string key, string expectedDefault)
    {
        ComposeLines.Count(l => l.TrimStart().StartsWith($"{key}:", StringComparison.Ordinal))
            .ShouldBe(1, $"deploy/docker-compose.yml must carry exactly one `{key}:` line.");

        CaddyBlock().Select(l => l.Trim())
            .ShouldContain($"{key}: ${{{key}:-{expectedDefault}}}",
                $"`{key}` must default to `{expectedDefault}` inside the `caddy` service. Any other " +
                "default changes the edge on the next reconcile without an operator choosing it.");
    }

    /// <summary>
    /// Rollback R1 in runbook §3f is one key, <c>SITE_ADMISSION=basic_auth</c>, only while the
    /// credential is still required with admission open: an empty pair adapts and gates everyone out,
    /// a half-empty one crash-loops the edge.
    /// </summary>
    [Theory]
    [InlineData("BASIC_AUTH_USER")]
    [InlineData("BASIC_AUTH_HASH")]
    public void TheGateCredential_StaysRequired_InEveryMode(string key)
        => CaddyBlock().Select(l => l.Trim()).ShouldContain($"{key}: ${{{key}:?}}");

    [Fact]
    public void TheEnvTemplate_NeverSetsAnAdmissionValue()
        => EnvExample.Split('\n').Select(l => l.TrimEnd('\r'))
            .ShouldNotContain(
                l => l.StartsWith("SITE_ADMISSION=", StringComparison.Ordinal),
                "a copied template must leave SITE_ADMISSION unset, so the compose default gates.");

    [Theory]
    [InlineData("SITE_ADMISSION", "basic_auth")]
    [InlineData("SITE_ALIASES", "none")]
    public void EdgeMode_IsDocumentedInTheEnvTemplate_AsAnOptionalKeyWithItsDefault(
        string key, string expectedDefault)
    {
        EnvExample.Split('\n').Select(l => l.TrimEnd('\r'))
            .ShouldContain($"#{key}={expectedDefault}",
                $"deploy/.env.example must list `{key}` commented out with its default, so the " +
                "operator setting it at the domain move finds the key and the value it replaces.");
    }
}
