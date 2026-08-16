using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins that the deploy stack's registration gate defaults to CLOSED.
///
/// <para>
/// <c>AuthOptions</c>' own default is <c>false</c>, and
/// <c>AuthOptionsValidatorTests</c> pins every combination the validator accepts and refuses.
/// Neither of them sees the value the box actually feeds it: an interpolation default in
/// <c>deploy/docker-compose.yml</c>. Flip <c>${AUTH_REGISTRATIONS_OPEN:-false}</c> to
/// <c>:-true</c> and nothing goes red — the box boots LEGALLY, because open + confirmation +
/// a delivering sender satisfies both validator rules, and registration is then open on a
/// public IP with no operator having flipped anything.
/// </para>
///
/// <para>
/// This project rather than a shell fixture, and the reason is measured: the deploy suites do
/// not render the compose file (<c>jobbliggaren-reconcile.test.sh</c> stubs
/// <c>compose config</c>), while this project already copies <c>deploy/docker-compose.yml</c>
/// into its output for <see cref="DeployComposeRoleTests"/> — same "nothing reads the two
/// files together" gap, one file over. Text assertions for the same reason stated there: a
/// compose file is not .NET configuration, and the value under test IS a literal.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeRegistrationGateTests
{
    private static string ComposeText =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"));

    private static string LineContaining(string key) =>
        ComposeText.Split('\n').SingleOrDefault(l => l.Contains(key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"deploy/docker-compose.yml has no single line containing '{key}'. If the file was " +
            "restructured, this pin must be rewritten rather than deleted.");

    [Theory]
    [InlineData("Auth__RegistrationsOpen:", "AUTH_REGISTRATIONS_OPEN")]
    [InlineData("Auth__RequireEmailConfirmation:", "AUTH_REQUIRE_EMAIL_CONFIRMATION")]
    public void AuthFlags_DefaultToFalse_WhenTheBoxSetsNothing(string key, string variable)
    {
        // The whole line, so the assertion fails on a changed variable name as well as on a
        // changed default — a rename that leaves the .env template behind is the same defect
        // as an open default, just slower to find.
        LineContaining(key).Trim().ShouldBe($"{key} ${{{variable}:-false}}",
            customMessage:
            "The gate must default CLOSED. An open default boots legally once the mail " +
            "provider delivers, so nothing refuses it and no log line reads as wrong: the " +
            "announcement says OPEN because it IS open. AuthOptions' code default and " +
            "AuthOptionsValidator both sit downstream of this value and cannot see it.");
    }

    [Fact]
    public void AdminBootstrapEmail_DefaultsToEmpty_SoNoAccountIsGrantedAdminByDeploy()
    {
        // IdempotentAdminRoleSeeder assigns the role at EVERY start to whichever account
        // matches. A non-empty default would make the deploy file itself an authorization
        // grant, re-asserted on every restart and surviving in-app revocation.
        LineContaining("AdminBootstrap__InitialAdminEmail:").Trim()
            .ShouldBe("AdminBootstrap__InitialAdminEmail: ${ADMIN_BOOTSTRAP_INITIAL_ADMIN_EMAIL:-}",
                customMessage:
                "An address here grants Admin from the deploy file rather than from an " +
                "operator's deliberate .env edit.");
    }

    [Fact]
    public void TheAuthFlags_ReachTheApiOnly_AndNeverTheWorker()
    {
        // Counting occurrences is NOT this property. Move all three keys into a shared anchor
        // and each still appears once, the whole-line pins above still match, and the worker
        // silently gains config it has no consumer for — the exact arrangement the compose
        // comment says it avoids. So assert WHERE they sit: after the `api:` service header and
        // before the next top-level service begins.
        var lines = ComposeText.Split('\n');
        var apiStart = Array.FindIndex(lines, l => l.StartsWith("  api:", StringComparison.Ordinal));
        apiStart.ShouldBeGreaterThan(-1, "the compose file no longer declares an `api` service");

        var apiEnd = Array.FindIndex(
            lines, apiStart + 1,
            l => l.Length > 2 && l.StartsWith("  ", StringComparison.Ordinal)
                 && l[2] != ' ' && l.TrimEnd().EndsWith(':'));
        if (apiEnd < 0) apiEnd = lines.Length;

        foreach (var key in new[]
                 {
                     "Auth__RegistrationsOpen:",
                     "Auth__RequireEmailConfirmation:",
                     "AdminBootstrap__InitialAdminEmail:",
                 })
        {
            var at = Array.FindIndex(lines, l => l.Contains(key, StringComparison.Ordinal));
            at.ShouldBeInRange(apiStart, apiEnd - 1,
                $"'{key}' must sit inside the api service block. Outside it — in an anchor, or " +
                "under worker — it reaches a host that consumes no Auth__* and registers no " +
                "validator for it.");

            lines.Count(l => l.Contains(key, StringComparison.Ordinal)).ShouldBe(1,
                $"'{key}' must be declared exactly once; a second declaration is a second home " +
                "that can drift from this one.");
        }
    }
}
