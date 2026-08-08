using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Migrate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// Pins that <see cref="JobAdBrowsePlanFixture"/> actually migrated under production's privilege
/// posture (#1232).
///
/// <para>
/// <b>Why this is its own test class rather than an implicit property of the plan guard.</b> The
/// fixture's migration now runs as <see cref="Roles.App"/> against a Phase A-provisioned
/// database, which is what makes it an oracle for the two 42501s #1229 repaired. But if that
/// posture silently reverted to superuser, every plan test would still pass — the oracle would
/// go quiet rather than red, which is the exact failure mode the extraction exists to end.
/// So the posture gets an assertion with its own name, which fails for its own reason.
/// </para>
///
/// <para>
/// These read <c>current_user</c> and the catalog rather than re-stating any grant: the grants
/// themselves are pinned in <c>PhaseADatabaseGrantsTests</c> and <c>PhaseASchemaGrantsTests</c>
/// against the production objects. What is asserted here is the thing those unit tests cannot
/// see — that a real migration really ran as that role, against a database really provisioned
/// that way.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
public class JobAdBrowsePlanFixturePostureTests(JobAdBrowsePlanFixture fixture)
{
    private NpgsqlConnection OpenAppConnection()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = new NpgsqlConnection(db.Database.GetConnectionString());
        conn.Open();
        return conn;
    }

    private T ScalarAs<T>(string sql)
    {
        using var conn = OpenAppConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        return (T)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Fixture_MigratesAsTheAppRole_NotAsTheTestcontainersSuperuser()
    {
        // The single fact the whole change turns on. Before #1232 this returned `postgres`.
        ScalarAs<string>("SELECT current_user;").ShouldBe(Roles.App);
    }

    [Fact]
    public void Fixture_RunsWithoutSuperuser()
    {
        // A superuser bypasses every privilege check, so a fixture connected as one would pass
        // the assertion above by name while still being blind by construction.
        ScalarAs<bool>("SELECT usesuper FROM pg_user WHERE usename = current_user;")
            .ShouldBeFalse();
    }

    [Fact]
    public void Fixture_Database_WithholdsCreate_FromTheAppRole()
    {
        // ADR 0034: no role below master may add a schema. This is what proves the database was
        // provisioned by Phase A rather than left at Postgres' defaults — where PUBLIC (and so
        // every role) holds both CREATE and TEMPORARY on the database.
        ScalarAs<bool>("SELECT has_database_privilege(current_user, current_database(), 'CREATE');")
            .ShouldBeFalse();
    }

    [Fact]
    public void Fixture_Database_GrantsTemporary_ToTheAppRole()
    {
        // The grant whose deletion left every suite green while a clean-database boot died with
        // 42501. Two applied migrations create temp tables, so this is load-bearing for
        // MigrateAsync itself — and until this fixture ran as a non-superuser, nothing measured
        // it against a real database.
        ScalarAs<bool>("SELECT has_database_privilege(current_user, current_database(), 'TEMPORARY');")
            .ShouldBeTrue();
    }

    [Fact]
    public void Fixture_PublicSchema_GrantsCreate_ToTheAppRole()
    {
        // The other 42501: `permission denied for schema public`. EF Core's migrator needs
        // CREATE here, and PhaseASchemaGrants.PublicSchema's first statement is what grants it.
        ScalarAs<bool>("SELECT has_schema_privilege(current_user, 'public', 'CREATE');")
            .ShouldBeTrue();
    }
}
