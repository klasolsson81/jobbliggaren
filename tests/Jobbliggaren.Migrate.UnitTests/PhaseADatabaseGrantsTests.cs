using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins Phase A's database-level privilege model.
///
/// <para>
/// A database carries exactly three privileges — <c>CREATE</c>, <c>TEMPORARY</c>,
/// <c>CONNECT</c> — and Phase A revokes all three from <c>PUBLIC</c> before handing back a
/// deliberate subset. Which subset is a decision (ADR 0034), and until
/// <see cref="PhaseADatabaseGrants"/> existed it was expressed only as a sequence of awaits
/// inside a local function in top-level statements, which no test assembly can reach. Deleting
/// the <c>TEMPORARY</c> grant left every suite green while a clean-database boot died with
/// 42501 — measured on the Netcup box 2026-08-05, and the reason this file exists.
/// </para>
///
/// <para>
/// The privileges are enumerable, so the assertions are stated as a closed set rather than as
/// spot checks: three privileges times three roles is a nine-cell table, and every cell below
/// is either granted or withheld on purpose. A fourth privilege cannot go missing silently
/// because there is no fourth privilege.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class PhaseADatabaseGrantsTests
{
    private const string Db = "jobbliggaren";

    private static IReadOnlyList<string> Sql() =>
        [.. PhaseADatabaseGrants.For(Db).Select(s => s.Sql)];

    [Fact]
    public void For_RevokesEveryPrivilegeFromPublic_Before_GrantingAnythingBack()
    {
        var sql = Sql();

        sql[0].ShouldBe($"REVOKE ALL ON DATABASE \"{Db}\" FROM PUBLIC;");
        // Order is load-bearing: a revoke issued after the grants would undo them.
        sql.Skip(1).ShouldAllBe(s => s.StartsWith("GRANT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Roles.Migrations)]
    [InlineData(Roles.App)]
    [InlineData(Roles.Worker)]
    public void For_GrantsConnect_ToEveryRole(string role)
    {
        Sql().ShouldContain($"GRANT CONNECT ON DATABASE \"{Db}\" TO {role};");
    }

    [Fact]
    public void For_GrantsTemporary_ToTheAppRole()
    {
        // The app role is the one `schema` mode connects as, and two applied migrations create
        // temp tables. Without this grant a database Phase A provisioned cannot be migrated:
        // 42501 "permission denied to create temporary tables".
        Sql().ShouldContain($"GRANT TEMPORARY ON DATABASE \"{Db}\" TO {Roles.App};");
    }

    [Theory]
    [InlineData(Roles.Migrations)]
    [InlineData(Roles.Worker)]
    public void For_WithholdsTemporary_FromEveryOtherRole(string role)
    {
        // Neither runs migrations, so neither needs it. Stated as an assertion rather than left
        // implicit: the grant above is narrow ON PURPOSE, and a later widening should have to
        // delete a test that says why.
        Sql().ShouldNotContain($"GRANT TEMPORARY ON DATABASE \"{Db}\" TO {role};");
    }

    [Theory]
    [InlineData(Roles.Migrations)]
    [InlineData(Roles.App)]
    [InlineData(Roles.Worker)]
    public void For_NeverGrantsCreateOnDatabase_ToAnyRole(string role)
    {
        // ADR 0034 forbids CREATE ON DATABASE for the app role; the schemas the application
        // needs are created by Phase A itself under master credentials. Asserted for all three
        // because the ADR's reasoning — nothing below master should be able to add a schema —
        // is not specific to one of them.
        Sql().ShouldNotContain($"GRANT CREATE ON DATABASE \"{Db}\" TO {role};");
        Sql().ShouldNotContain($"GRANT ALL ON DATABASE \"{Db}\" TO {role};");
    }

    [Fact]
    public void For_DescribesEveryStatement_ForTheOperatorLog()
    {
        // Phase A logs one line per statement. A blank description would make a failed init
        // unreadable at exactly the moment someone is reading it.
        PhaseADatabaseGrants.For(Db)
            .ShouldAllBe(s => !string.IsNullOrWhiteSpace(s.Description));
    }

    [Theory]
    [InlineData("jobbliggaren\"; DROP DATABASE x; --")]
    [InlineData("Jobbliggaren")]
    [InlineData("")]
    public void For_RejectsAnIdentifierItCannotSafelyInterpolate(string dbName)
    {
        // Postgres cannot parameterise an identifier, so this type interpolates one. The
        // extraction that gave it a testable surface also separated it from the caller's guard;
        // it now enforces its own precondition rather than documenting it.
        Should.Throw<InvalidOperationException>(() => PhaseADatabaseGrants.For(dbName));
    }

    [Fact]
    public void For_InterpolatesTheDatabaseName_IntoEveryDatabaseLevelStatement()
    {
        // The vacuity guard. Every assertion above embeds "jobbliggaren" in its expected
        // string, so a For() that ignored its argument and returned statements against some
        // other database would fail them all — but only by accident of the constant. This says
        // it directly, against a name the production path never uses.
        PhaseADatabaseGrants.For("other_db")
            .ShouldAllBe(s => s.Sql.Contains("\"other_db\"", StringComparison.Ordinal));
    }
}
