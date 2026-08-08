using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins Phase A's schema-level privilege model (ADR 0034).
///
/// <para>
/// <b>These assertions are bound to the PRIVILEGE SPACE, not to statement strings, and that is
/// deliberate.</b> The sibling <c>PhaseADatabaseGrantsTests</c> spot-checks with
/// <c>ShouldNotContain("GRANT TEMPORARY ON DATABASE … TO x;")</c>, which
/// <c>GRANT TEMPORARY, CONNECT ON DATABASE … TO x;</c> passes while granting TEMPORARY — a
/// filed defect (#1230). Writing this larger surface in the same form would knowingly
/// propagate it, so every statement here is parsed into
/// (privileges, target, grantee) and asserted against the resulting table. Retrofitting the
/// database-level tests to this form stays #1230; this file is the reference form.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public partial class PhaseASchemaGrantsTests
{
    /// <summary>A parsed privilege statement: who gets which privileges on what.</summary>
    private sealed record Access(string Action, IReadOnlyList<string> Privileges, string Target, string Grantee);

    // GRANT <privs> ON [ALL TABLES IN |ALL SEQUENCES IN ]SCHEMA <name> TO <role>;
    [GeneratedRegex(@"^GRANT (?<privs>.+?) ON (?:ALL (?:TABLES|SEQUENCES) IN )?SCHEMA (?<target>\w+) TO (?<grantee>\w+);$")]
    private static partial Regex GrantOnSchema();

    // REVOKE <privs> ON SCHEMA <name> FROM <grantee>;
    [GeneratedRegex(@"^REVOKE (?<privs>.+?) ON SCHEMA (?<target>\w+) FROM (?<grantee>\w+);$")]
    private static partial Regex RevokeOnSchema();

    // ALTER DEFAULT PRIVILEGES IN SCHEMA <name> GRANT <privs> ON <kind> TO <role>;
    [GeneratedRegex(@"^ALTER DEFAULT PRIVILEGES IN SCHEMA (?<target>\w+) GRANT (?<privs>.+?) ON (?:TABLES|SEQUENCES) TO (?<grantee>\w+);$")]
    private static partial Regex DefaultPrivileges();

    private static List<Access> Parse(IEnumerable<PrivilegeStatement> statements)
    {
        var parsed = new List<Access>();
        foreach (var sql in statements.Select(s => s.Sql))
        {
            var (action, m) =
                GrantOnSchema().Match(sql) is { Success: true } g ? ("GRANT", g)
                : DefaultPrivileges().Match(sql) is { Success: true } d ? ("GRANT", d)
                : RevokeOnSchema().Match(sql) is { Success: true } r ? ("REVOKE", r)
                : (string.Empty, Match.Empty);

            if (action.Length == 0)
            {
                // CREATE SCHEMA … AUTHORIZATION … conveys ownership, not a grant. Skipped here
                // and asserted separately, so an unparsed GRANT cannot hide in this branch.
                sql.ShouldStartWith("CREATE SCHEMA", Case.Sensitive,
                    $"Unparsed statement — the privilege-space assertions below cannot see it: {sql}");
                continue;
            }

            parsed.Add(new Access(
                action,
                [.. m.Groups["privs"].Value.Split(',').Select(p => p.Trim())],
                m.Groups["target"].Value,
                m.Groups["grantee"].Value));
        }

        return parsed;
    }

    private static List<Access> Public() => Parse(PhaseASchemaGrants.PublicSchema);
    private static List<Access> Hangfire() => Parse(PhaseASchemaGrants.HangfireSchema);
    private static List<Access> Identity() => Parse(PhaseASchemaGrants.IdentitySchema);

    // ---------------------------------------------------------------------------------------
    // The vacuity guard comes FIRST, because every "withholds"/"grants nothing" assertion below
    // passes for free if the parser silently matched nothing. A regex that stops matching is
    // exactly how a privilege guard goes quiet instead of red.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Parser_ExtractsAnAccessRow_ForEveryNonCreateStatement()
    {
        Public().Count.ShouldBe(PhaseASchemaGrants.PublicSchema.Count);       // 5, no CREATE SCHEMA
        Hangfire().Count.ShouldBe(PhaseASchemaGrants.HangfireSchema.Count - 1);  // 3 - 1 CREATE SCHEMA
        Identity().Count.ShouldBe(PhaseASchemaGrants.IdentitySchema.Count - 1);  // 7 - 1 CREATE SCHEMA

        // And the privileges actually came out as a LIST, not as one uncut blob — the precise
        // thing #1230's substring form cannot do.
        Public().ShouldContain(a => a.Privileges.Count > 1);
    }

    // ---------------------------------------------------------------------------------------
    // public — the schema EF Core's migrator writes, as jobbliggaren_app.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void PublicSchema_GrantsCreate_ToTheAppRole()
    {
        // The statement #1229 repaired: `schema` mode connected as a role holding no CREATE
        // here and died with 42501. Asserted through the parsed privilege LIST, so the
        // real statement's "USAGE, CREATE" satisfies it and a future "USAGE" alone would not.
        Public().ShouldContain(a =>
            a.Action == "GRANT" && a.Target == "public"
            && a.Grantee == Roles.App && a.Privileges.Contains("CREATE"));
    }

    [Theory]
    [InlineData(Roles.Migrations)]
    [InlineData(Roles.Worker)]
    public void PublicSchema_GrantsNothing_ToAnyOtherRole(string role)
    {
        // Only the app role runs EF migrations. Grantee-based rather than statement-based, so
        // adding `role` to an existing statement's TO-list would still fail this.
        Public().ShouldNotContain(a => a.Grantee == role);
    }

    // ---------------------------------------------------------------------------------------
    // hangfire — owned and written by jobbliggaren_migrations; the worker's DML is Phase C.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void HangfireSchema_GrantsUsageAndCreate_ToTheMigrationsRole()
    {
        Hangfire().ShouldContain(a =>
            a.Action == "GRANT" && a.Target == "hangfire" && a.Grantee == Roles.Migrations
            && a.Privileges.Contains("USAGE") && a.Privileges.Contains("CREATE"));
    }

    [Theory]
    [InlineData(Roles.App)]
    [InlineData(Roles.Worker)]
    public void HangfireSchema_GrantsNothing_ToTheAppOrWorkerRole(string role)
    {
        // The worker's hangfire.* DML arrives in Phase C, after Hangfire's installer has created
        // the tables — deliberately not here. The app role never touches hangfire at all.
        Hangfire().ShouldNotContain(a => a.Action == "GRANT" && a.Grantee == role);
    }

    // ---------------------------------------------------------------------------------------
    // identity — ADR 0034, the schema AppIdentityDbContext declares.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void IdentitySchema_GrantsUsageAndCreate_ToTheAppRole()
    {
        Identity().ShouldContain(a =>
            a.Action == "GRANT" && a.Target == "identity" && a.Grantee == Roles.App
            && a.Privileges.Contains("USAGE") && a.Privileges.Contains("CREATE"));
    }

    [Theory]
    [InlineData(Roles.Migrations)]
    [InlineData(Roles.Worker)]
    public void IdentitySchema_GrantsNothing_ToAnyOtherRole(string role)
    {
        Identity().ShouldNotContain(a => a.Grantee == role);
    }

    // ---------------------------------------------------------------------------------------
    // Cross-cutting posture.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void CreatedSchemas_RevokeEverythingFromPublic_BeforeGrantingAnythingBack()
    {
        // Both schemas Phase A creates are closed to PUBLIC first. Order is load-bearing: a
        // revoke issued after the grants would undo them.
        foreach (var list in new[] { PhaseASchemaGrants.HangfireSchema, PhaseASchemaGrants.IdentitySchema })
        {
            var sql = list.Select(s => s.Sql).ToList();
            sql[0].ShouldStartWith("CREATE SCHEMA");
            sql[1].ShouldStartWith("REVOKE ALL ON SCHEMA");
            sql.Skip(2).ShouldAllBe(s => s.StartsWith("GRANT", StringComparison.Ordinal)
                || s.StartsWith("ALTER DEFAULT PRIVILEGES", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void PublicSchema_CarriesNoRevokeFromPublic_AndThatAsymmetryIsPreExisting()
    {
        // `public` ships with the database, so Phase A neither creates it nor revokes PUBLIC's
        // USAGE on it — unlike the two schemas it does create. On PG15+ PUBLIC no longer holds
        // CREATE on schema public, but it retains USAGE. This is stated as an assertion rather
        // than left implicit so that closing the asymmetry is a deliberate change with a test to
        // delete, and not a silent one. Whether it SHOULD be closed is a posture question for
        // security-auditor, not a defect this extraction introduced.
        Public().ShouldNotContain(a => a.Action == "REVOKE");
    }

    [Fact]
    public void EveryStatement_DescribesItself_ForTheOperatorLog()
    {
        // Phase A logs one line per statement. A blank description would make a failed init
        // unreadable at exactly the moment someone is reading it.
        PhaseASchemaGrants.HangfireSchema
            .Concat(PhaseASchemaGrants.PublicSchema)
            .Concat(PhaseASchemaGrants.IdentitySchema)
            .ShouldAllBe(s => !string.IsNullOrWhiteSpace(s.Description));
    }

    [Fact]
    public void NoStatement_GrantsAnythingToPublic()
    {
        // The whole point of Phase A is that PUBLIC ends up with nothing. A grant back to PUBLIC
        // anywhere in the schema lists would undo the database-level revoke for every role at
        // once, including ones that do not exist yet.
        Public().Concat(Hangfire()).Concat(Identity())
            .ShouldNotContain(a => a.Action == "GRANT"
                && a.Grantee.Equals("PUBLIC", StringComparison.OrdinalIgnoreCase));
    }
}
