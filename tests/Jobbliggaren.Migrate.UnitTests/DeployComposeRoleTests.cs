using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins which database role the deploy stack hands each migrate connection string.
///
/// <para>
/// The roles are not interchangeable and the difference is invisible until a clean database
/// boots. <c>ExecutePhaseAAsync</c> grants <c>USAGE, CREATE ON SCHEMA public</c> — plus ALL on
/// its tables and sequences, plus the matching default privileges — to <see cref="Roles.App"/>
/// and to no other role. <c>schema</c> mode applies the app schema's EF migrations through
/// <c>MIGRATE_APP_CONNECTION_STRING</c>, so pointing that variable at
/// <see cref="Roles.Migrations"/> fails with <c>42501: permission denied for schema public</c>
/// the moment EF creates <c>__EFMigrationsHistory</c>. Measured on the Netcup box 2026-08-05,
/// first boot — and the compose file had shipped that way since the stack landed, because
/// nothing reads the two files together.
/// </para>
///
/// <para>
/// This is a text assertion, deliberately: a compose file is not .NET configuration, so there
/// is no binding engine to run it through, and the value under test IS a literal. What the test
/// buys is the coupling — it reads the role name from <c>Roles</c>, the same constants the
/// GRANTs use, so renaming a role cannot leave the compose file behind silently.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeRoleTests
{
    private static string ComposeText =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"));

    /// <summary>The value after the first colon, trimmed — never the key or the whole line.</summary>
    private static string ValueOf(string key) =>
        LineContaining(key).Split(':', 2)[1].Trim();

    private static string LineContaining(string key) =>
        ComposeText.Split('\n').SingleOrDefault(l => l.Contains(key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"deploy/docker-compose.yml has no single line containing '{key}'. If the file was " +
            "restructured, this pin must be rewritten rather than deleted.");

    [Fact]
    public void MigrateAppConnectionString_NamesTheRoleThatOwnsSchemaPublic()
    {
        // ONE line again, and deliberately: `migrate` and `migrate-rewrap` share the
        // x-migrate-app-connection anchor, so there is exactly one definition to pin. An
        // earlier version of this test asserted a count of two matching lines, which caught
        // the role but let host, port, database and SSL mode drift between the copies — and
        // made every legitimate new consumer fail on a number rather than on a property.
        var line = LineContaining("MIGRATE_APP_CONNECTION_STRING:");

        line.ShouldContain($"Username={Roles.App}", Case.Sensitive,
            customMessage:
            "schema mode applies the app schema's EF migrations, and ExecutePhaseAAsync grants " +
            "CREATE on schema public to the app role only. Any other role fails with 42501 on " +
            "a database this tool provisioned.");
    }

    [Fact]
    public void MigrateMasterUsername_IsThePostgresSuperuser()
    {
        // The vacuity guard: the role split only means something if the master credential is a
        // DIFFERENT identity from the one the test above pins.
        //
        // Assert the VALUE, not the line. Shouldly's ShouldContain defaults to a
        // case-insensitive comparison, and the whole line contains the variable name — so
        // `MIGRATE_MASTER_USERNAME: ${POSTGRES_MASTER_USER:?}` satisfied the earlier form by
        // matching "POSTGRES" inside the placeholder, certifying an identity it never read.
        // Measured by code-reviewer against this test: 19/19 green on that edit.
        ValueOf("MIGRATE_MASTER_USERNAME:").ShouldBe("postgres");

        // And the migrations role must still be PROVISIONED even though — after the repair this
        // file pins — no connection string connects AS it. `init` creates all three roles from
        // the passwords the service is handed, and the migrations role owns the hangfire schema
        // (runbook §4 GRANT model). Asserting a `Username=` for it would be false: that was this
        // test's own first draft, and it went red against the very repair it ships with.
        ComposeText.ShouldContain("MIGRATE_MIGRATIONS_PASSWORD",
            customMessage:
            "init provisions three roles; drop this and the hangfire GRANT model has no owner.");
    }
}
