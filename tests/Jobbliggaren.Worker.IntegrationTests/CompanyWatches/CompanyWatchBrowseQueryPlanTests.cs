using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.CompanyWatches;

/// <summary>
/// #560 kriterie-vågen PR-2 — THE PIN. Proves that the browse predicate is actually served by
/// <c>ix_company_register_sni_codes_gin</c>, the GIN index PR-1 shipped.
///
/// <para>
/// <b>Why this test is the point of the whole PR.</b> The sibling suite
/// (<see cref="CompanyWatchBrowseQueryTests"/>) proves the browse returns the RIGHT ROWS. It would go
/// on passing, green and silent, if the port emitted the natural-looking
/// <c>.Where(c =&gt; c.SniCodes.Any(s =&gt; userSni.Contains(s)))</c> — which Npgsql compiles to an
/// <c>unnest</c> subquery that CANNOT use a GIN index. Same rows, right answers, and PR-1's index
/// reduced to decoration nobody would notice until the register had 1,17M rows in it. That is the
/// vacuous-guarantee class this codebase has already shipped twice (the never-written
/// <c>JobAd.DeletedAt</c> filter, #805-3; the Art. 17 erasure that could erase nothing, #842). This
/// test is what makes the index a fact rather than an intention.
/// </para>
///
/// <para>
/// <b>The oracle runs the EXACT command production runs.</b> It does not re-type the SQL — it calls
/// <c>CompanyWatchBrowseQuery.Build{Items,Count}Command</c> and prefixes <c>EXPLAIN</c>. A hand-copied
/// query string is not an oracle, and this is not hypothetical: <c>Jobbliggaren.Migrate</c>'s
/// <c>explain-search</c> tool hand-wrote its SQL, silently drifted from the production predicate, and
/// (in its own comment) "lied in the REASSURING direction". The factories carry the parameter TYPES as
/// well as the text — binding <c>@sni</c> as <c>text</c> rather than <c>text[]</c> would EXPLAIN a
/// different plan entirely.
/// </para>
///
/// <para>
/// <b>The assertion is POSITIVE on the index NAME, never negative on "no Seq Scan"</b> — and that
/// distinction is the difference between a real pin and a fake one. Under
/// <c>enable_seqscan = off</c> the NAIVE unnest shape still has index paths available to it (the
/// <c>sate_kommun_code</c> and <c>status</c> btrees), so its plan contains a Bitmap Index Scan and NO
/// <c>Seq Scan</c> at all. A <c>ShouldNotContain("Seq Scan")</c> assertion would therefore PASS under
/// the mutation — reproducing #805-3 inside the very instrument built to prevent it
/// (dotnet-architect Q1(a), 2026-07-13).
/// </para>
///
/// <para>
/// <b>What this proves, precisely: index ELIGIBILITY, not production's plan choice.</b>
/// <c>enable_seqscan = off</c> is a test-only instrument. Production must NOT set it: the bound-legal
/// worst case (1000 SNI codes × 290 kommuner) genuinely matches most of the register, and a sequential
/// scan IS the right plan there. What the pin guarantees is that the predicate the port emits is a
/// shape the GIN index CAN serve — which is exactly what the naive LINQ form silently is not.
/// </para>
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class CompanyWatchBrowseQueryPlanTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private const string GinIndexName = "ix_company_register_sni_codes_gin";
    private const string OverlapOperator = "&&";

    /// <summary>
    /// #875 — the btree that lets the planner walk the ORDER BY in index order and stop at LIMIT 20,
    /// instead of sorting the whole match set. Its existence is why the Sort-Key pin above had to change
    /// shape: the old pin justified itself with "there is no index on (company_name,
    /// organization_number)", and that sentence is now false.
    /// </summary>
    private const string NameIndexName = "ix_company_register_company_name_organization_number";

    private static readonly DateTimeOffset T0 = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    // Every seeded row sits in this kommun and is Active → both btree predicates have selectivity ≈ 1.0
    // and are worthless to the planner. Only the SNI axis discriminates.
    private const string SeededKommun = "0180";
    private const string ProbeSni = "62010";
    private const string FillerSni = "99999";
    private const int SeededRows = 2000;
    private const int ProbeMatches = 2;

    [Fact]
    public async Task ItemsQuery_UsesTheSniGinIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        var plan = await ExplainAsync(
            ctx.Db,
            (conn, spec) => CompanyWatchBrowseQuery.BuildItemsCommand(conn, spec, page: 1, pageSize: 20),
            ct);

        AssertServedByGin(plan, "items");

        // The ORDER BY must be TOTAL, and this is the deterministic way to pin it (code-reviewer Major,
        // 2026-07-13). The sibling suite's page-boundary test seeds duplicate company_names and asserts
        // that no row is lost or duplicated across pages — but whether a MISSING tiebreak would actually
        // make that test fail is PLAN-DEPENDENT (it relies on top-N heapsort ordering the ties
        // differently at OFFSET 0/10/20; "can diverge" is not "does diverge"). That is an asserted
        // non-vacuity, not a demonstrated one.
        //
        // TRUTH-SYNC (#875, 2026-07-14). This comment used to justify itself with "there is no index on
        // (company_name, organization_number), so a Sort node always sits above the bitmap heap scan".
        // #875 CREATES that index, so the sentence is now FALSE — exactly the load-bearing-comment rot
        // this suite exists to prevent, and it would have shipped silently.
        //
        // The pin survives because it asserts the PROPERTY, not the plan shape: the total order must be
        // carried by SOMETHING. Two plans can carry it, and both are legitimate:
        //   (a) an explicit Sort node naming both keys (what a SELECTIVE criterion gets — the planner
        //       stays on BitmapAnd(GIN, kommun) and sorts the handful of hits), or
        //   (b) an ordered walk of the new (company_name, organization_number) index, which IS the total
        //       order — no Sort node needed (what a BROAD criterion gets; see
        //       BroadCriterion_WalksTheNameIndexInOrder_AndStopsEarly).
        // Anything else means the OFFSET walk is over a non-total order, and rows silently vanish and
        // duplicate across pages.
        //
        // Still mutation-verified: drop `, organization_number` from ItemsSql and (a) loses its second
        // key while (b) cannot match the index — both branches fail.
        var carriesTotalOrder =
            plan.Contains("Sort Key: company_name, organization_number", StringComparison.Ordinal)
            || plan.Contains(NameIndexName, StringComparison.Ordinal);

        carriesTotalOrder.ShouldBeTrue(
            "The items query's ORDER BY is no longer TOTAL. company_name is not unique in a real "
            + "register (duplicate legal names are normal) and Postgres sorts are not stable, so an "
            + "OFFSET walk over a non-total order silently drops and duplicates rows ACROSS pages. "
            + "organization_number is the PK — it is what makes the order total. The plan must carry "
            + $"that order EITHER as 'Sort Key: company_name, organization_number' OR as an ordered walk "
            + $"of {NameIndexName}.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    [Fact]
    public async Task BroadCriterion_WalksTheNameIndexInOrder_AndStopsEarly()
    {
        // THE guarantee #875 ships, and it needs its own pin — the GIN pin above cannot see it.
        //
        // Without ix_company_register_company_name_organization_number, a criterion matching a large
        // share of the register forces Postgres to SORT the whole match set to answer LIMIT 20. Measured
        // against 1,17M rows in production's actual post-sync state (GIN's fastupdate pending list full,
        // which is what the register looks like right after the nightly SCB sync): the bound-legal worst
        // case took p95 = 7 066 ms against ADR 0045's 300 ms budget — 23x over, and a connection-pool
        // exhaustion vector for a single authenticated user (security-auditor, #560 PR-2).
        //
        // With the index the planner walks it IN ORDER and the LIMIT stops it after 20 rows:
        //     Limit  (cost=1.15..26.51 rows=20)
        //       ->  Index Scan using ix_company_register_company_name_organization_number
        //             Filter: (sni_codes && ... AND status = 'Active' AND sate_kommun_code = ANY ...)
        // p95 drops to 26 ms. That plan shape IS the fix — so it is what gets pinned, not the latency.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // A criterion matching MOST of the seeded table — the shape that makes the sort expensive.
        //
        // It MUST be the broad one. The selective probe the sibling test uses correctly keeps
        // BitmapAnd + Sort, so it CANNOT — by construction — notice that this index has fallen out of
        // the plan. A pin that cannot fail for the reason it exists is not a pin.
        var broad = CompanyWatchCriteriaSpec.FromTrusted([ProbeSni, FillerSni], [SeededKommun]);

        var plan = await ExplainSpecAsync(ctx.Db, broad, ct);

        // POSITIVE on the index name — never a negative "no Seq Scan" (dotnet-architect Q1(a): the
        // negative form passes under mutation because other index paths remain).
        plan.ShouldContain(
            NameIndexName,
            customMessage: BrokenPlanMessage(plan));

        // ...AND the Sort node must be GONE. The index name alone is not enough: a plan could reach the
        // index and STILL sort on top of it, which would mean the ordered walk is not being used as the
        // ordering — i.e. the whole point is lost while the name-check stays green.
        plan.ShouldNotContain(
            "Sort Key:",
            customMessage:
                "The broad criterion's plan still SORTS. Reaching the index is not the guarantee — "
                + "WALKING it in order and stopping at LIMIT 20 is. A Sort node above the scan means the "
                + $"whole match set is still being ordered.{Environment.NewLine}{BrokenPlanMessage(plan)}");
    }

    private static string BrokenPlanMessage(string plan) =>
        $"A BROAD criterion no longer walks {NameIndexName} in order. It is therefore sorting the "
        + "entire match set to answer LIMIT 20 — which is what took the bound-legal worst case to "
        + "7 066 ms p95 against ADR 0045's 300 ms budget (measured against 1,17M rows in the register's "
        + "post-sync state) before #875 added the index. The 55 MB index is then dead weight, and every "
        + "other test stays green while it happens."
        + Environment.NewLine
        + Environment.NewLine
        + "MOST LIKELY CAUSE — THE COLLATION. A btree is built WITH a collation. If you changed the "
        + "collation of `ORDER BY company_name` (e.g. to COLLATE \"sv-SE-x-icu\" so that Å/Ä/Ö sort "
        + "after Z, which they currently do NOT — issue #884), the index no longer serves that sort and "
        + "Postgres drops it SILENTLY. Rebuild the index in the SAME migration that changes the sort. "
        + "Other causes: another column added to the ORDER BY; Max Auto Prepare enabling generic plans "
        + "that move the cost estimates; a statistics change that makes bitmap+sort look cheaper."
        + Environment.NewLine
        + $"Plan:{Environment.NewLine}{plan}";

    [Fact]
    public async Task CountQuery_UsesTheSniGinIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // The count is a SECOND command text and therefore a SECOND plan (and since the CTO's capped-count
        // fix it is a subquery shape, not a bare aggregate) — pinning only the items query would leave the
        // count free to regress into a full scan on its own.
        var plan = await ExplainAsync(
            ctx.Db, (conn, spec) => CompanyWatchBrowseQuery.BuildCountCommand(conn, spec, pageSize: 20), ct);

        AssertServedByGin(plan, "count");
    }

    private static void AssertServedByGin(string plan, string which)
    {
        // POSITIVE on the index name. If this ever fails, the message names the index that is missing
        // from the plan — which is the whole diagnostic.
        plan.ShouldContain(
            GinIndexName,
            customMessage:
                $"The {which} query's plan does NOT use {GinIndexName}. The GIN index PR-1 shipped is "
                + "then cosmetic: the browse still returns the right rows, so every other test stays "
                + "green while the register scans. The usual cause is the predicate no longer being "
                + "emitted as the array-overlap operator (LINQ compiles the natural form to an unnest "
                + $"subquery, which no GIN index can serve).{Environment.NewLine}Plan:{Environment.NewLine}{plan}");

        // ...and on the OPERATOR, so the pin fails if the index is reached through some other predicate
        // shape (e.g. a well-meaning switch to @> containment, which would also be GIN-servable but is
        // the WRONG semantics — see CompanyWatchBrowseQueryTests.Browse_PartialSniOverlap_IsAMatch).
        plan.ShouldContain(
            OverlapOperator,
            customMessage:
                $"The {which} query's plan does not carry the array-overlap operator "
                + $"'{OverlapOperator}'.{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    /// <summary>EXPLAINs the items query for an arbitrary spec (the broad-criterion pin needs one).</summary>
    private static Task<string> ExplainSpecAsync(
        AppDbContext db, CompanyWatchCriteriaSpec spec, CancellationToken ct) =>
        ExplainAsync(
            db,
            (conn, s) => CompanyWatchBrowseQuery.BuildItemsCommand(conn, s, page: 1, pageSize: 20),
            ct,
            spec);

    private static async Task<string> ExplainAsync(
        AppDbContext db,
        Func<NpgsqlConnection, CompanyWatchCriteriaSpec, NpgsqlCommand> build,
        CancellationToken ct,
        CompanyWatchCriteriaSpec? specOverride = null)
    {
        var spec = specOverride ?? CompanyWatchCriteriaSpec.FromTrusted([ProbeSni], [SeededKommun]);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // SET LOCAL needs a transaction block, and scoping it there means the GUC cannot leak into a
        // sibling test on this shared connection.
        await using var tx = await connection.BeginTransactionAsync(ct);

        // On a 2000-row table a sequential scan is genuinely the cheapest plan, so without this the
        // planner would pick one no matter how usable the index is — and the pin would be untestable.
        // Penalising seq scan (it is a cost penalty, not a prohibition) forces the planner to reveal
        // WHICH index path it can reach the predicate through, if any.
        await using (var guc = connection.CreateCommand())
        {
            guc.Transaction = tx;
            guc.CommandText = "SET LOCAL enable_seqscan = off;";
            await guc.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = build(connection, spec);
        cmd.Transaction = tx;
        // EXPLAIN, not EXPLAIN ANALYZE: this is a PLANNER assertion. Row-level truth is the semantic
        // suite's job (SoC), and not executing the query keeps the pin fast and quiet.
        cmd.CommandText = "EXPLAIN " + cmd.CommandText;

        var lines = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                lines.Add(reader.GetString(0));
        }

        await tx.RollbackAsync(ct);
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<ScopedContext> SeededContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The "Worker" collection runs serially over ONE Postgres → this test can own the table, which
        // is what makes the seeded selectivity skew (and therefore the plan) deterministic.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);

        // Deliberate selectivity skew: only ~0.1% of rows carry the probe SNI, while EVERY row matches
        // the kommun and status predicates. The GIN path is then the only SELECTIVE one, so the planner
        // picks it unambiguously instead of coin-flipping against the kommun btree.
        var entries = Enumerable.Range(0, SeededRows)
            .Select(i => new ScbCompanyRegisterEntry
            {
                OrganizationNumber = $"55{i:D8}",
                Name = $"Företag {i:D4} AB",
                SeatMunicipalityCode = SeededKommun,
                SeatMunicipalityName = "Stockholm",
                SniCodes = [i < ProbeMatches ? ProbeSni : FillerSni],
                HasAdvertisingBlock = false,
                ScbStatusRaw = "1",
                Status = CompanyRegisterStatus.Active,
            })
            .ToList();

        // Seed through the production write path (the same bulk upsert the nightly SCB sync uses).
        await new ScbCompanyRegisterStore(db).UpsertBatchAsync(entries, T0, ct);

        // MANDATORY, not hygiene. TRUNCATE wipes the statistics; without ANALYZE the planner falls back
        // on default selectivity constants (≈0.005 for `= ANY`, ≈0.01 for `&&`) that sit close enough
        // together to make the index choice arbitrary — a flaky pin. With real stats the array MCE
        // gives `&&` its true ~0.001 and the kommun MCV gives ~1.0, and the choice is forced.
        await db.Database.ExecuteSqlRawAsync("ANALYZE company_register;", ct);

        return new ScopedContext(scope, db);
    }

    private sealed class ScopedContext(AsyncServiceScope scope, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public AsyncServiceScope Scope { get; } = scope;
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}
