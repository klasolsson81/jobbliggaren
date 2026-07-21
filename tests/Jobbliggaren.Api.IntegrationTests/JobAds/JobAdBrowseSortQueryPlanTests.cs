using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1013 — the production-cardinality plan-CHOICE guard for the default anonymous <c>/jobb</c> browse.
///
/// <para>
/// <b>What it adds over the #743 eligibility oracle.</b>
/// <see cref="JobAdPlannerUsabilityOracleTests"/> proves the index
/// <c>ix_job_ads_status_published_at_id</c> <i>can</i> serve both halves of the browse, using
/// <c>enable_seqscan=off; enable_sort=off;</c> — PostgreSQL 17+ <c>disabled_nodes</c> PROHIBITIONS that
/// make the pin statistics-independent (the #821/#875 charter). That answers "is the index USABLE", not
/// "does the planner CHOOSE it at corpus scale". This test answers the second question: it EXPLAINs the
/// browse with the planner's FULL search space — NO GUC — against a production-scale row regime, and
/// asserts the planner actually picks the ordered index scan and pays no top-N heapsort of the ~42k
/// active rows a page view would otherwise sort.
/// </para>
///
/// <para>
/// <b>Why a dedicated container (see <see cref="JobAdBrowsePlanFixture"/>).</b> A no-GUC choice is only
/// deterministic if the test owns the table's statistics. The shared <c>[Collection("Api")]</c> Postgres
/// accumulates job_ads from ~62 seeding classes and never truncates, so a no-GUC choice there would flake
/// between Index Scan and Bitmap + Sort at the execution-order-dependent row estimate — the very flake
/// ADR 0045 Beslut 5 forbids ("a flaky perf-gate is worse than no perf-gate"). This guard therefore lives
/// in its own single-owner container, mirroring how <c>CompanyWatchBrowseQueryPlanTests</c> owns
/// <c>company_register</c> in its serial Worker collection.
/// </para>
///
/// <para>
/// <b>The assertion is POSITIVE on the index NAME, never negative on "no Seq Scan".</b> A
/// <c>ShouldNotContain("Seq Scan")</c> would pass under mutation because other index paths (the FTS/trigram
/// indexes) remain available — reproducing the vacuous-guarantee class this repo has shipped before
/// (#805-3, #842). The pin is <c>ShouldContain(index)</c> AND <c>ShouldNotContain("Sort Key:")</c>: reaching
/// the index is not enough — the ordered WALK that <c>LIMIT 20</c> stops early is the guarantee, and a Sort
/// node above the scan means the whole active set is still being ordered.
/// </para>
///
/// <para>
/// <b>The EXPLAINed shape carries the REAL list projection, not <c>SELECT id</c>.</b> A choice guard is
/// projection-sensitive: projection width drives heap-fetch cost, which can tip Index Only Scan ↔ Index
/// Scan ↔ bitmap. So <see cref="BrowseSortSql"/> selects the exact columns
/// <c>JobAdSearchComposition.ToDto()</c> projects for the list surface (post-#745: no <c>description</c>),
/// under <c>JobAdSearchComposition.ApplyFilter</c>'s <c>status = 'Active'</c> and
/// <c>ApplySort(PublishedAtDesc)</c>'s <c>published_at DESC, id</c>, at <c>ListJobAdsQuery.PageSize</c> = 20.
/// It is a truth-synced hand-written constant (kept in step with that composition); a hand-copied shape
/// that drifts would EXPLAIN the wrong plan — see the sibling's <c>explain-search</c> cautionary tale.
/// </para>
///
/// <para>
/// <b>The plan choice is SCALE-DEPENDENT and this test says so.</b> At tiny active-row counts the planner
/// legitimately prefers Seq Scan + Sort (sorting a handful of rows is cheaper than an index walk); the
/// #743 eligibility oracle's docblock reproduces exactly that flip. The guarantee this test pins is the
/// production regime, so it seeds a production-representative corpus + ANALYZE before EXPLAINing.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class JobAdBrowseSortQueryPlanTests(JobAdBrowsePlanFixture fixture)
{
    private readonly JobAdBrowsePlanFixture _fixture = fixture;

    private const string IndexName = "ix_job_ads_status_published_at_id";

    // Production-representative corpus. Production carries ~42k active ads; ~80% Active / ~20% Archived here
    // yields ~40k active — the regime in which the index decisively wins over Seq Scan + top-N heapsort.
    // MEASURED, not guessed (ADR 0045 Beslut 5, the sibling's discipline): #1013's own measurement found the
    // no-GUC plan picks the index at N≥200-with-ANALYZE and at 50000, degrading to Seq Scan + Sort only at
    // tiny N. 50000 sits far above that flip and mirrors production scale.
    private const int TotalRows = 50_000;

    // Truth-synced to the production default browse (JobAdSearchComposition.ToDto + ApplyFilter + ApplySort;
    // ListJobAdsQuery.PageSize = 20). Real projection columns (NOT SELECT id) so the choice is measured under
    // production's heap-fetch cost. If ToDto()/ApplyFilter/ApplySort change, this constant changes with them.
    private const string BrowseSortSql =
        "SELECT j.id, j.title, j.company_name, j.url, j.source, j.status, "
        + "j.published_at, j.expires_at, j.created_at "
        + "FROM job_ads AS j "
        + "WHERE j.status = 'Active' "
        + "ORDER BY j.published_at DESC, j.id "
        + "LIMIT 20";

    [Fact]
    public async Task DefaultBrowseSort_PicksTheBrowseSortIndex_AtProductionCardinality_NoGuc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SeedProductionRegimeAsync(db, ct);

        // NO GUC — the planner keeps its whole search space, including the Seq Scan + Sort plan the #743
        // index exists to beat. A choice made inside enable_seqscan/enable_sort = off is not production's.
        var plan = await ExplainAsync(db, BrowseSortSql, ct);

        // Positive on the index name (a negative "no Seq Scan" passes under mutation while other index paths
        // remain — dotnet-architect Q1(a), #875).
        plan.ShouldContain(IndexName, Case.Insensitive, BrokenPlanMessage(plan));

        // ...AND the Sort node must be gone: reaching the index while still Sorting means the ordered walk
        // is not serving the order, and the ~40k-row heapsort is still paid per page.
        plan.ShouldNotContain("Sort Key:", Case.Insensitive, SortSurvivedMessage(plan));
    }

    /// <summary>
    /// TRUNCATE-and-own → bulk-seed a production-scale regime → ANALYZE. Bulk INSERT (not the domain factory):
    /// job_ads carries no DEK-encrypted column, so a raw insert is faithful and fast at 50k, and this seeds a
    /// PLANNER regime, not a semantic fixture. <c>published_at</c>, <c>status</c> and <c>company_name</c> are
    /// deterministically decorrelated from insertion order so the planner does not price the index's heap
    /// fetches as sequential — i.e. does not flatter the exact plan under test (the sibling's discipline).
    /// <c>search_vector</c> / <c>extracted_lexemes</c> are STORED generated columns and are omitted.
    /// </summary>
    private static async Task SeedProductionRegimeAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(300);

        // Dedicated single-owner container ⇒ this class owns the table; TRUNCATE makes the seeded regime
        // (and therefore the plan) deterministic.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        var seed =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, remote) "
            + "SELECT gen_random_uuid(), "
            + "'Systemutvecklare ' || i, "
            + "'Beskrivning ' || i || '. ' || repeat('Arbetsuppgifter och kvalifikationer. ', 4), "
            + "'https://example.com/jobb/' || i, "
            // published_at spread across ~N minutes, decorrelated from i so heap fetches are not priced as
            // sequential (an ascending published_at would flatter the index scan under test).
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            // ~20% Archived, interleaved so Active rows are not physically clustered.
            + "CASE WHEN (i * 7919) % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Foretag ' || ((i * 7919) % " + TotalRows + ") || ' AB', "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(seed, ct);

        // MANDATORY, not hygiene. TRUNCATE wipes the statistics; without ANALYZE the planner falls back on
        // default selectivity constants and the index choice becomes arbitrary — a flaky pin.
        await db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
    }

    /// <summary>
    /// EXPLAIN (not EXPLAIN ANALYZE — a PLANNER assertion, row-level truth is the semantic suite's job) with
    /// NO GUC. No transaction: there is no SET LOCAL to scope.
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

    private static string BrokenPlanMessage(string plan) =>
        $"The default /jobb browse no longer chooses {IndexName} at production cardinality with the planner's "
        + "full search space. It is therefore Seq-Scanning the Active set and top-N heapsorting it to answer "
        + "LIMIT 20 — the ~42k-row-per-page sort #743's index exists to remove. Note the choice is "
        + "scale-dependent: at tiny active-row counts Seq Scan + Sort is legitimately cheaper (the #743 "
        + "eligibility oracle reproduces that flip), which is why this test seeds a production regime + ANALYZE."
        + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}";

    private static string SortSurvivedMessage(string plan) =>
        "The browse plan reaches the index but STILL sorts (a Sort Key line is present) at production "
        + "cardinality with no GUC. Reaching the index is not the guarantee — WALKING it in order and stopping "
        + "at LIMIT 20 is. A surviving Sort node means the whole active set is still being ordered."
        + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}";
}
