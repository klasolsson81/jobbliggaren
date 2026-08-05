using System.Globalization;

namespace Jobbliggaren.Migrate;

/// <summary>
/// The database-level privilege statements Phase A issues, as data.
///
/// <para>
/// Extracted from <c>ExecutePhaseAAsync</c> so the privilege model has a reader. A database
/// carries exactly three privileges — <c>CREATE</c>, <c>TEMPORARY</c>, <c>CONNECT</c> — and
/// Phase A revokes all three from <c>PUBLIC</c> and then hands back a deliberate subset. Which
/// subset is a decision (ADR 0034), and until this type existed it was expressed only as a
/// sequence of awaits inside a local function in top-level statements, which no test assembly
/// can reach. Deleting the <c>TEMPORARY</c> grant left every suite green while a clean-database
/// boot died with 42501 — measured on the Netcup box 2026-08-05.
/// </para>
///
/// <para>
/// <c>CREATE</c> is withheld from all three roles on purpose and there is no statement for it
/// here: ADR 0034 forbids <c>CREATE ON DATABASE</c> for the app role, and the schemas the app
/// needs are created by Phase A itself under master credentials.
/// </para>
/// </summary>
internal static class PhaseADatabaseGrants
{
    /// <summary>One statement plus the operator-facing description Phase A logs for it.</summary>
    internal readonly record struct Statement(string Sql, string Description);

    /// <summary>
    /// The database-level sequence, in execution order. Validates <paramref name="dbName"/>
    /// itself: Postgres cannot parameterise an identifier, and the extraction that gave this
    /// type its own testable surface also separated it from the caller's guard — a precondition
    /// documented but not enforced is one the next caller does not know about.
    /// </summary>
    internal static IReadOnlyList<Statement> For(string dbName)
    {
        PostgresIdentifier.Validate(dbName);

        return
        [
        new(
            string.Create(CultureInfo.InvariantCulture, $"REVOKE ALL ON DATABASE \"{dbName}\" FROM PUBLIC;"),
            "Revoke PUBLIC från db"),

        .. new[] { Roles.Migrations, Roles.App, Roles.Worker }.Select(role => new Statement(
            string.Create(CultureInfo.InvariantCulture, $"GRANT CONNECT ON DATABASE \"{dbName}\" TO {role};"),
            string.Create(CultureInfo.InvariantCulture, $"GRANT CONNECT till {role}"))),

        // TEMPORARY back to the app role, and it is the REVOKE above that makes this necessary.
        // A database grants TEMP to PUBLIC by default; the revoke takes it from every
        // non-superuser at once, and the CONNECT loop hands back only `c`. Two applied
        // migrations create temp tables (C2SearchParityReverseLookupAndRecentExpansion,
        // MaterialiseJobAdSourceFacets), so `schema` mode — which runs as the app role and is a
        // gating dependency of api and worker on EVERY `up` — dies with 42501 on any database
        // this very phase provisioned. Measured on the box: "permission denied to create
        // temporary tables in database jobbliggaren", after `datacl` showed
        // {postgres=CTc, migrations=c, app=c, worker=c}.
        //
        // Only the app role: it is the one that runs migrations. A temp table is session-scoped
        // and vanishes with the connection, so this is narrower than the CREATE on schema public
        // the app role already holds.
        new(
            string.Create(CultureInfo.InvariantCulture, $"GRANT TEMPORARY ON DATABASE \"{dbName}\" TO {Roles.App};"),
            string.Create(CultureInfo.InvariantCulture, $"GRANT TEMPORARY till {Roles.App}")),
        ];
    }
}
