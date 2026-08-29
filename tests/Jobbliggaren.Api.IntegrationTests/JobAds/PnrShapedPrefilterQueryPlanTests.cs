using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1558 follow-up — THE PIN for <c>ix_job_ads_org_nr_pnr_shaped</c>, the partial index that stops the
/// enskild-firma token resolution from scanning <c>job_ads</c>.
///
/// <para>
/// <b>Why the index needs a pin at all.</b> The index is PARTIAL, and a partial index is only usable when
/// PostgreSQL can PROVE that the query's predicate implies the index's. That proof is structural: it
/// survives a reordering of the conjuncts, but not a change of shape. Rewrite the handler's
/// prefilter — <c>Substring(2, 1)</c> to a <c>LIKE</c>, the two <c>||</c> disjuncts to a <c>Contains</c>
/// over an array, the <c>Length == 10</c> guard dropped as "redundant" — and the proof fails. Nothing
/// breaks: the rows are identical, every semantic test stays green, and the index quietly stops being
/// used while the scan cost goes back to growing with the table. That is the vacuous-guarantee class this
/// repo has shipped twice already (#805-3, #842), and this test is the only thing that can see it.
/// </para>
///
/// <para>
/// <b>The two assertions carry different halves, and BOTH are required.</b> The positive
/// <c>using &lt;index&gt;</c> proves the planner reaches the index. The absent <c>Filter:</c> proves the
/// implication was PROVEN rather than merely survivable: if the index is chosen but the predicate is
/// re-applied per row, the partial form bought a smaller heap and nothing else. It is the same shape as
/// the sibling's <c>ShouldContain("using " + IndexName)</c> + <c>ShouldNotContain("Sort Key:")</c> — a
/// positive on the index name, never a negative on "no Seq Scan", which passes under mutation while other
/// index paths remain (dotnet-architect Q1(a), #875).
/// </para>
///
/// <para>
/// <b>What this test does NOT catch, stated plainly.</b> The SQL below is a hand-written THIRD copy of a
/// predicate that already exists twice in LINQ (<c>ListCompanyWatchesQueryHandler</c> and
/// <c>CompanyWatchScanJob</c>, duplicated deliberately — single-sourcing was declined 2026-07-18 because
/// it would force a predicate combinator off the BUILD.md §3.1 allowlist). Only the FIRST of those is
/// pinned here: the scan job's copy sits inside an OR and cannot use this index at all. If the handler's
/// copy drifts and this constant does not, the pin keeps passing against a shape production no longer
/// emits. That is the
/// <c>explain-search</c> cautionary tale the sibling suite names, and it is inherited here rather than
/// solved. The migration's docblock carries the same warning from the third side.
/// </para>
///
/// <para>
/// <b>No GUC.</b> This is a plan-CHOICE claim, not an eligibility claim: at production cardinality the
/// planner must pick this index with its full search space available. A choice made inside
/// <c>enable_seqscan = off</c> is not production's choice.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class PnrShapedPrefilterQueryPlanTests(JobAdBrowsePlanFixture fixture)
{
    private readonly JobAdBrowsePlanFixture _fixture = fixture;

    private const string IndexName = "ix_job_ads_org_nr_pnr_shaped";

    private const int TotalRows = 50_000;

    // Sole proprietorships are rare by construction, and the ratio is the point: the index is worth having
    // precisely because the matching set is a sliver of the table. Both boundary third-digits (0 and 1) are
    // seeded — the superset semantics each LINQ copy is separately oracle-pinned on.
    private const int PnrShapedRows = 20;

    /// <summary>
    /// Truth-synced to the prefilter in <c>ListCompanyWatchesQueryHandler</c> — that handler ONLY. The
    /// scan job writes the same three conjuncts but as one arm of an OR under a status/recency guard, so
    /// its predicate does not imply the index predicate and it is out of this pin's scope entirely.
    /// STATUS-AGNOSTIC on purpose — a followed company keeps its name whether
    /// or not its ads are Active, so the token must still resolve for an archived-only enskild firma. A
    /// status predicate here would EXPLAIN a query production does not run.
    /// </summary>
    private const string PrefilterSql =
        "SELECT DISTINCT j.organization_number "
        + "FROM job_ads AS j "
        + "WHERE j.organization_number IS NOT NULL "
        + "AND length(j.organization_number) = 10 "
        + "AND substring(j.organization_number, 3, 1) IN ('0', '1')";

    [Fact]
    public async Task PnrShapedPrefilter_IsServedByThePartialIndex_WithThePredicateAbsorbed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SeedProductionRegimeAsync(db, ct);

        var plan = await ExplainAsync(db, PrefilterSql, ct);

        plan.ShouldContain("using " + IndexName, Case.Insensitive, NotUsedMessage(plan));
        plan.ShouldNotContain("Filter:", Case.Insensitive, FilterSurvivedMessage(plan));
    }

    /// <summary>
    /// TRUNCATE-and-own then bulk-seed a production-scale regime then ANALYZE. The ANALYZE is MANDATORY,
    /// not hygiene: TRUNCATE wipes the statistics, and without them the planner falls back on default
    /// selectivity constants and the choice becomes arbitrary — a flaky pin rather than a guard.
    /// </summary>
    private static async Task SeedProductionRegimeAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(300);

        // The collection is single-owner and serialised, so this class owns the table at its turn.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        // Legal-entity org.nrs: the third digit is forced outside the pnr-shaped superset. Without that
        // the "rare sliver" premise would not hold and the index would not be the obvious pick.
        var bulk =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, "
            + "organization_number, remote) "
            + "SELECT gen_random_uuid(), "
            + "'Systemutvecklare ' || i, "
            + "'Beskrivning ' || i, "
            + "'https://example.com/jobb/' || i, "
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            + "CASE WHEN (i * 7919) % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Foretag ' || i || ' AB', "
            + "'55' || lpad(((i * 7919) % 100000000)::text, 8, '0'), "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(bulk, ct);

        // The sliver: both boundary third-digits, and Archived among them so the status-agnostic premise
        // is exercised rather than assumed.
        var pnr =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, "
            + "organization_number, remote) "
            + "SELECT gen_random_uuid(), "
            + "'Enskild firma ' || i, "
            + "'Beskrivning ' || i, "
            + "'https://example.com/ef/' || i, "
            + "now(), now(), "
            + "CASE WHEN i % 2 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Firma ' || i, "
            + "'19' || (i % 2)::text || lpad(i::text, 7, '0'), "
            + "false "
            + "FROM generate_series(0, " + (PnrShapedRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(pnr, ct);

        await db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
    }

    /// <summary>
    /// EXPLAIN, not EXPLAIN ANALYZE: this asserts what the PLANNER chose, and row-level truth is the
    /// semantic suite's job. No GUC, so there is no SET LOCAL to scope to a transaction.
    /// </summary>
    private static async Task<string> ExplainAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXPLAIN " + sql;

        var lines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(reader.GetString(0));

        return string.Join(Environment.NewLine, lines);
    }

    private static string NotUsedMessage(string plan) =>
        $"The personnummer-shape prefilter no longer reaches {IndexName}. Either the index is gone, or a "
        + "LINQ copy of the predicate drifted from the shape the index was built for and this constant was "
        + "not updated with it. The enskild-firma token resolution is scanning job_ads again, so its cost "
        + "grows with the table rather than with the user's watch set."
        + Environment.NewLine + "Plan:" + Environment.NewLine + plan;

    private static string FilterSurvivedMessage(string plan) =>
        $"{IndexName} is reached, but PostgreSQL is still re-applying the predicate per row — the "
        + "implication proof failed, so the partial form bought a smaller heap and nothing else. The usual "
        + "cause is a conjunct written in one shape here and another shape in the index predicate."
        + Environment.NewLine + "Plan:" + Environment.NewLine + plan;
}
