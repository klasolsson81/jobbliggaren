using Jobbliggaren.Migrate;
using Npgsql;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Provisions a Testcontainers database with <b>production's own Phase A privilege posture</b>,
/// and hands back a connection string for the app role.
///
/// <para>
/// <b>Why this exists (#1232).</b> A fixture that migrates as the Testcontainers superuser,
/// against a database where <c>REVOKE ALL ON DATABASE … FROM PUBLIC</c> never ran, cannot see
/// privilege defects <i>by construction</i>. Both 42501s that PR #1229 repaired —
/// <c>permission denied for schema public</c> and
/// <c>permission denied to create temporary tables</c> — were found by booting the stack on the
/// Netcup box, not by CI, because CI's only migration oracle ran as superuser.
/// </para>
///
/// <para>
/// <b>The grants are not restated here.</b> Every statement executed below comes from
/// <see cref="PhaseADatabaseGrants"/> and <see cref="PhaseASchemaGrants"/> — the same objects
/// <c>Jobbliggaren.Migrate</c>'s <c>ExecutePhaseAAsync</c> iterates. That is the whole point: a
/// hand-written copy would be a second truth that drifts silently, and asserting a production
/// privilege fact against a fixture's own DDL is the premise-production-cannot-produce shape
/// CLAUDE.md §5 <c>Tests:</c> forbids. Delete a grant in production and this provisioner stops
/// issuing it too, which is what makes the oracle an oracle.
/// </para>
///
/// <para>
/// <b>Role creation IS hand-written, and that is the deliberate line.</b>
/// <c>CREATE ROLE … LOGIN PASSWORD</c> is provisioning mechanism rather than privilege model:
/// it establishes an identity rather than asserting a production fact, and production's own
/// helper takes operator passwords this fixture must not have. What must match production is
/// which privileges those identities end up holding — and that is exactly what is borrowed.
/// </para>
/// </summary>
public static class TestDatabaseProvisioner
{
    /// <summary>
    /// Throwaway password for the three roles. These roles exist only inside an ephemeral
    /// container that is destroyed with the fixture, and the container is not reachable off-box;
    /// production's passwords are operator-provisioned and never appear in this repo.
    /// </summary>
    private const string TestRolePassword = "test-only-not-a-secret";

    /// <summary>
    /// Runs Phase A against <paramref name="superuserConnectionString"/> and returns a
    /// connection string authenticating as <see cref="Roles.App"/> — the role production's
    /// <c>schema</c> mode connects as, and therefore the role EF Core's migrator must run as
    /// for a migration to prove anything about privileges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mirrors production's boot order, and skips two phases on purpose.</b> Real <c>init</c>
    /// also runs Hangfire's schema installer as <see cref="Roles.Migrations"/> (Phase B) and the
    /// worker's <c>hangfire.*</c> DML grants (Phase C). A persistence-slice fixture has neither
    /// Hangfire nor an <c>AppIdentityDbContext</c>, so <see cref="PhaseASchemaGrants.HangfireSchema"/>
    /// and <see cref="PhaseASchemaGrants.IdentitySchema"/> are not issued either. Omitting them
    /// creates no false premise for the assertion this enables — "AppDbContext's migrations
    /// apply as the app role" — because no migration in that context touches those schemas. It
    /// is written down so the omission reads as a decision rather than an oversight.
    /// </para>
    /// </remarks>
    public static async Task<string> ProvisionAndGetAppConnectionStringAsync(
        string superuserConnectionString,
        CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder(superuserConnectionString);
        var dbName = builder.Database
            ?? throw new InvalidOperationException("Connection string names no database.");

        await using (var conn = new NpgsqlConnection(superuserConnectionString))
        {
            await conn.OpenAsync(ct);

            // Identities first: a role cannot be granted anything before it exists.
            //
            // An in-server DO-block guard, because Postgres has no CREATE ROLE IF NOT EXISTS and a
            // second call against the same database throws 42710. One caller today, but this file
            // is linked into more than one test project on purpose and the next adopter is who
            // would meet it.
            //
            // NOT the shape production uses, and deliberately so. CreateRoleIfNotExistsAsync does a
            // client-side parameterised SELECT then a separate DDL statement, and its own comment
            // rejects anonymous DO blocks precisely because pl/pgsql takes no Npgsql parameters.
            // Here the values are compile-time constants, so that constraint does not apply — and
            // role creation is the half this type declares as hand-written rather than mirrored.
            // Two differences worth knowing: production also ALTERs an existing role's password,
            // this only creates when absent; and SELECT-then-CREATE is not atomic, so the EXCEPTION
            // handler is what makes two concurrent provisioners against one server safe.
            foreach (var role in new[] { Roles.Migrations, Roles.App, Roles.Worker })
            {
                await ExecuteAsync(
                    conn,
                    $"""
                     DO $$ BEGIN
                       IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                         CREATE ROLE {role} LOGIN PASSWORD '{TestRolePassword}';
                       END IF;
                     EXCEPTION WHEN duplicate_object THEN NULL;
                     END $$;
                     """,
                    ct);
            }

            // Production's statements, in production's order.
            foreach (var statement in PhaseADatabaseGrants.For(dbName).Concat(PhaseASchemaGrants.PublicSchema))
            {
                await ExecuteAsync(conn, statement.Sql, ct);
            }
        }

        return new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = Roles.App,
            Password = TestRolePassword,
        }.ConnectionString;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
