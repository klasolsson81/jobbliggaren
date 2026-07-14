using System.Text.RegularExpressions;
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
/// <b><see cref="BroadCriterion_WalksTheNameIndexInOrder_AndStopsEarly"/> has TWO jobs, and the second
/// is invisible (#884, 2026-07-14).</b> It pins the plan SHAPE — an ordered walk that LIMIT stops early
/// rather than a full Sort. It is ALSO the only test anywhere in the repo that can see a COLLATION
/// MISMATCH between <c>company_name</c>'s column collation (<c>swedish</c>, ICU sv-SE, pinned by #884)
/// and the collation the INDEX was built under. Mutation-measured, not asserted:
/// <code>
/// mutation                                          OrdersByATotalKey  Browse_SortsSwedish  THIS TEST
/// COLLATE "en_US.utf8" written into ItemsSql        RED                RED                  RED
/// a divergent COLLATE on the INDEX (column intact)  green              green                RED  (alone)
/// </code>
/// The second row is the whole reason this test cannot be deleted, and it is NOT the case the earlier
/// draft of this paragraph named. An index-side divergence changes neither the SQL text, nor the rows
/// returned, nor their order — the browse stays perfectly correct. It simply Sorts the entire match set
/// to produce twenty rows (7 066 ms against ADR 0045's 300 ms budget), and every other test in the repo
/// stays green while it does. <b>Do not delete this as "just a perf pin".</b>
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
/// <b>What the GIN pins prove, precisely: index ELIGIBILITY, not production's plan choice.</b>
/// <c>enable_seqscan = off</c> is a test-only instrument, and on PostgreSQL 17+ (this repo runs 18.3)
/// it is an effective PROHIBITION, not a cost penalty — the planner counts <c>disabled_nodes</c> and
/// prefers any path with fewer of them regardless of cost. Production must NOT set it. What those pins
/// guarantee is that the predicate the port emits is a shape the GIN index CAN serve — which is exactly
/// what the naive LINQ form silently is not.
/// </para>
///
/// <para>
/// <b>The #875 pins claim something different, and are instrumented differently.</b>
/// <see cref="BroadCriterion_WalksTheNameIndexInOrder_AndStopsEarly"/> claims a plan CHOICE, so it runs
/// with the planner's FULL search space — no GUC — because a choice made inside a prohibition is not
/// production's choice (code-reviewer, 2026-07-14).
/// <see cref="ItemsQuery_OrdersByATotalKey"/> claims a property of the SQL and asserts it on the SQL,
/// because the total order stopped being observable in the plan the moment the name index existed.
/// <see cref="GenericPlan_DoesNotUseTheNameIndex_SoMaxAutoPrepareWouldKillIt"/> is the only instrument
/// here that can see what happens when Postgres plans this statement WITHOUT the parameter values.
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
    }

    [Fact]
    public void ItemsQuery_OrdersByATotalKey()
    {
        // The ORDER BY must be TOTAL: company_name is not unique in a real register (duplicate legal
        // names are normal) and Postgres sorts are not stable, so an OFFSET walk over a non-total order
        // silently drops and duplicates rows ACROSS pages. organization_number is the PK — it is what
        // makes the order total.
        //
        // THIS IS PINNED ON THE SQL, NOT ON THE PLAN — and #875 is why (both review gates, 2026-07-14).
        // Before this PR the plan could carry the proof: with no index on (company_name,
        // organization_number) a Sort node was FORCED, and a Sort node NAMES its key. #875 creates that
        // index, and now:
        //   - an Index Scan can serve the ORDER BY with no Sort node at all, and
        //   - EXPLAIN does NOT print which columns an Index Scan orders on — not even under VERBOSE.
        // The total order therefore stopped being OBSERVABLE through the plan. The plan-based assertion
        // that used to guard it survived its own mutation only because the selective probe (2 hits of
        // 2000) makes the planner not CHOOSE the name index — a cost-model coincidence bound to
        // ProbeMatches = 2. Raise that to ~400 and the mutation would have begun passing silently: a
        // structural guarantee decayed into a disciplinary one, which is the exact defect class this
        // suite exists to catch.
        //
        // The order is a STATIC property of ItemsSql. Assert it there: immune to plan choice, collation,
        // statistics and Postgres version — and RED BY CONSTRUCTION under the mutation, forever.
        using var conn = new NpgsqlConnection();
        using var cmd = CompanyWatchBrowseQuery.BuildItemsCommand(
            conn, CompanyWatchCriteriaSpec.FromTrusted([ProbeSni], [SeededKommun]), page: 1, pageSize: 20);

        cmd.CommandText.ShouldContain(
            "ORDER BY company_name, organization_number",
            customMessage:
                "The items query's ORDER BY is no longer TOTAL. Postgres sorts are not stable and "
                + "company_name is not unique in a real register, so an OFFSET walk over a non-total "
                + "order silently drops and duplicates rows across pages. Keep organization_number (the "
                + "PK) as the tiebreak.");
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

        // A criterion matching EVERY seeded row (each carries ProbeSni or FillerSni) — the shape that
        // makes the sort expensive, and deliberately the most favourable one for an early stop.
        //
        // It MUST be the broad one. The selective probe the sibling test uses correctly keeps
        // BitmapAnd + Sort, so it CANNOT — by construction — notice that this index has fallen out of
        // the plan. A pin that cannot fail for the reason it exists is not a pin.
        var broad = CompanyWatchCriteriaSpec.FromTrusted([ProbeSni, FillerSni], [SeededKommun]);

        // NO enable_seqscan = off. This test claims a plan CHOICE, so it must let the planner have the
        // whole search space production has — including the Seq Scan -> Sort plan that took 7 066 ms.
        var plan = await ExplainSpecAsync(ctx.Db, broad, disableSeqScan: false, ct);

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

    [Fact]
    public async Task GenericPlan_DoesNotUseTheNameIndex_SoMaxAutoPrepareWouldKillIt()
    {
        // THE MINE, MADE VISIBLE. This is the only instrument in the repo that can see it, and it exists
        // because dotnet-architect refused to accept "plain btree is immune" as written (#875, 2026-07-14).
        //
        // ItemsSql is a CONSTANT. A selective criterion and a broad one send the SAME statement text and
        // differ only in the @sni/@kommun VALUES. Today Npgsql sends UNNAMED statements, so Postgres
        // custom-plans every execution with the actual values — and it picks TWO DIFFERENT PLANS:
        //     selective -> BitmapAnd(GIN, kommun) -> Sort        (correct: a handful of hits)
        //     broad     -> ordered walk of the name index        (correct: stop at LIMIT 20)
        //
        // docs/PERFORMANCE_AUDIT.md recommends enabling `Max Auto Prepare` in the Hetzner connection
        // strings. That makes the statement NAMED -> one plan-cache entry -> Postgres promotes it to a
        // GENERIC plan after a few executions. A generic plan is planned with NO parameter values, so it
        // must pick ONE plan for every criterion.
        //
        // MEASURED, not reasoned: it picks BitmapAnd + Sort. Which means the day Max Auto Prepare lands,
        // the broad and worst cases stop using this index entirely and fall back to sorting the whole
        // match set — 7 066 ms p95 against a 300 ms budget — and the 55 MB index becomes dead weight.
        // Every other test in this suite stays green, because they all EXPLAIN unnamed statements.
        //
        // This test pins TODAY'S generic plan. It is a characterisation pin: if it ever changes, someone
        // has changed something that moves the planner's cost model, and they need to know what it costs.
        // SEEDED AT PRODUCTION'S PLANNER REGIME, not the suite's 2 000-row one — and I only know that
        // matters because I got it wrong first. The generic plan's choice is SCALE-DEPENDENT: at 2 000
        // rows it DOES walk the name index; at 200 000 it drops it for BitmapAnd + Sort. A pin seeded at
        // 2 000 would have characterised an artefact of the test's own smallness and told the next person
        // the exact opposite of the truth.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await LargeSeededContextAsync(ct);

        var plan = await ExplainGenericPlanAsync(ctx.Db, ct);

        plan.ShouldNotContain(
            NameIndexName,
            customMessage:
                "The GENERIC plan now uses " + NameIndexName + ". That is a CHANGE, and it needs a "
                + "decision, not a green test. A generic plan serves EVERY criterion, so an ordered walk "
                + "of the name index would be applied to SELECTIVE criteria too — which is the exact "
                + "ORDER BY + LIMIT cliff (walk thousands of index entries per hit) that made this index "
                + "look dangerous in the first place. Re-measure the selective case under "
                + $"plan_cache_mode = force_generic_plan before accepting it.{Environment.NewLine}"
                + $"Plan:{Environment.NewLine}{plan}");

        // ...and pin WHY that is bad news rather than good: the generic plan SORTS.
        //
        // Matched as a SHAPE, not as a literal string — and that is a correction, not a preference.
        // This assertion used to demand the exact text "Sort Key: company_name, organization_number".
        // It broke the moment #884 pinned an ICU collation on the column, because EXPLAIN then renders
        // "Sort Key: company_name COLLATE swedish, organization_number". NOTHING about the plan had
        // changed — it still sorts, it still ignores the name index, the whole conclusion still holds.
        // Only Postgres's RENDERING moved. Worse, the failure message the old assertion printed said
        // "The GENERIC plan no longer sorts", which was simply false, and would have sent the next
        // reader hunting a behaviour change that never happened. A guard that goes red on cosmetics
        // while the thing it guards is intact is a guard that gets deleted by the person it wakes at
        // 2am — so it is now anchored to what it actually means: there is a Sort node, and it is
        // sorting on company_name.
        // Two tokens, in order, with the middle left open so a COLLATE annotation can sit between them.
        // `Sort Key:.*company_name` alone would also match "Sort Key: organization_number, company_name"
        // — a different sort key entirely, and one this test would then wave through.
        Regex.IsMatch(plan, @"Sort Key: company_name\b[^\r\n]*, organization_number\b").ShouldBeTrue(
            "The GENERIC plan neither walks " + NameIndexName + " NOR sorts on company_name — so it is "
            + "doing something this test has never seen, and the Max Auto Prepare warning in "
            + "docs/PERFORMANCE_AUDIT.md rests on it. Look at the plan before you trust it."
            + $"{Environment.NewLine}Plan:{Environment.NewLine}{plan}");
    }

    /// <summary>
    /// 200 000 rows — enough that the planner behaves the way it does against the 1,17M-row register.
    /// The suite's 2 000-row helper cannot reproduce that regime (see the generic-plan pin), and this
    /// seeds a PLANNER regime rather than a semantic fixture, so it bulk-inserts instead of upserting.
    /// Names are deterministically shuffled so <c>company_name</c> is not correlated with insertion
    /// order — a correlated column makes the planner price this index's heap fetches as sequential, i.e.
    /// flatters exactly the plan under test.
    /// </summary>
    private async Task<ScopedContext> LargeSeededContextAsync(CancellationToken ct)
    {
        var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.SetCommandTimeout(300);

        await db.Database.ExecuteSqlRawAsync("TRUNCATE company_register;", ct);

        var seed =
            "INSERT INTO company_register ("
            + "organization_number, company_name, sate_kommun_code, sate_kommun_name, "
            + "sni_codes, reklamsparr, scb_status_raw, status, synced_at, created_at) "
            + "SELECT lpad(i::text, 10, '0'), "
            + "'Företag ' || ((i * 7919) % 200000) || ' AB', "
            + "'" + SeededKommun + "', 'Stockholm', "
            + "ARRAY[CASE WHEN i % 1000 = 0 THEN '" + ProbeSni + "' ELSE '" + FillerSni + "' END], "
            + "false, '1', 'Active', now(), now() "
            + "FROM generate_series(0, 199999) AS i;";
        await db.Database.ExecuteSqlRawAsync(seed, ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_register;", ct);

        return new ScopedContext(scope, db);
    }

    /// <summary>
    /// EXPLAINs the items query under <c>plan_cache_mode = force_generic_plan</c> — i.e. what Postgres
    /// would settle on once <c>Max Auto Prepare</c> makes the statement named and cached. PREPARE takes
    /// <c>$n</c> placeholders, so production's command text is mechanically re-parameterised; the SQL
    /// BODY is production's, straight off <see cref="CompanyWatchBrowseQuery.BuildItemsCommand"/>.
    /// </summary>
    private static async Task<string> ExplainGenericPlanAsync(AppDbContext db, CancellationToken ct)
    {
        var spec = CompanyWatchCriteriaSpec.FromTrusted([ProbeSni, FillerSni], [SeededKommun]);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var tx = await connection.BeginTransactionAsync(ct);

        string sql;
        await using (var template = CompanyWatchBrowseQuery.BuildItemsCommand(connection, spec, 1, 20))
        {
            sql = template.CommandText
                .Replace("@status", "$1", StringComparison.Ordinal)
                .Replace("@kommun", "$2", StringComparison.Ordinal)
                .Replace("@sni", "$3", StringComparison.Ordinal)
                .Replace("@limit", "$4", StringComparison.Ordinal)
                .Replace("@offset", "$5", StringComparison.Ordinal)
                .TrimEnd(';', '\n', '\r', ' ');
        }

        await using (var prep = connection.CreateCommand())
        {
            prep.Transaction = tx;
            prep.CommandText =
                "PREPARE browse_generic(text, text[], text[], int, int) AS " + sql + ";"
                + " SET LOCAL plan_cache_mode = force_generic_plan;";
            await prep.ExecuteNonQueryAsync(ct);
        }

        var kommun = "ARRAY['" + SeededKommun + "']::text[]";
        var sni = $"ARRAY['{ProbeSni}','{FillerSni}']::text[]";

        var lines = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            // The literals only satisfy EXECUTE's arity — a generic plan is built without them.
            cmd.CommandText = $"EXPLAIN EXECUTE browse_generic('Active', {kommun}, {sni}, 20, 0);";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                lines.Add(reader.GetString(0));
        }

        await tx.RollbackAsync(ct);
        return string.Join(Environment.NewLine, lines);
    }

    private static string BrokenPlanMessage(string plan) =>
        $"A BROAD criterion no longer walks {NameIndexName} in order. It is therefore sorting the "
        + "entire match set to answer LIMIT 20 — which is what took the bound-legal worst case to "
        + "7 066 ms p95 against ADR 0045's 300 ms budget (measured against 1,17M rows in the register's "
        + "post-sync state) before #875 added the index. The 55 MB index is then dead weight, and every "
        + "other test stays green while it happens."
        + Environment.NewLine
        + Environment.NewLine
        + "MOST LIKELY CAUSE — THE COLLATION. A btree is built WITH a collation, and this one is built "
        + "with the COLUMN's: `swedish` (ICU sv-SE), pinned on company_name by #884. The index serves "
        + "ORDER BY company_name precisely because that sort INHERITS the same collation. Add an "
        + "explicit COLLATE \"...\" to the ORDER BY — any collation, including one that looks "
        + "equivalent — and the sort is now requested under a collation this index was not built under. "
        + "Postgres does not error. It silently Sorts the whole match set. DO NOT WRITE COLLATE IN THIS "
        + "QUERY: the column carries it, which is the whole point of putting it there. "
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
        AppDbContext db, CompanyWatchCriteriaSpec spec, bool disableSeqScan, CancellationToken ct) =>
        ExplainAsync(
            db,
            (conn, s) => CompanyWatchBrowseQuery.BuildItemsCommand(conn, s, page: 1, pageSize: 20),
            ct,
            spec,
            disableSeqScan);

    private static async Task<string> ExplainAsync(
        AppDbContext db,
        Func<NpgsqlConnection, CompanyWatchCriteriaSpec, NpgsqlCommand> build,
        CancellationToken ct,
        CompanyWatchCriteriaSpec? specOverride = null,
        bool disableSeqScan = true)
    {
        var spec = specOverride ?? CompanyWatchCriteriaSpec.FromTrusted([ProbeSni], [SeededKommun]);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // SET LOCAL needs a transaction block, and scoping it there means the GUC cannot leak into a
        // sibling test on this shared connection.
        await using var tx = await connection.BeginTransactionAsync(ct);

        // For the SELECTIVE probe on a 2000-row table a sequential scan is genuinely the cheapest plan,
        // so without this the planner would pick one no matter how usable the GIN index is — and the
        // eligibility pin would be untestable.
        //
        // TRUTH-SYNC (#875, 2026-07-14): this used to say "it is a cost penalty, not a prohibition".
        // FALSE on PostgreSQL 17+, which this repo runs (18.3). The enable_* GUCs no longer add a cost;
        // the planner counts `disabled_nodes` and prefers ANY path with fewer of them REGARDLESS of cost.
        // enable_seqscan = off is therefore an effective PROHIBITION as soon as an alternative exists.
        // That is fine for the GIN pins — they claim index ELIGIBILITY, and the docblock says so — but it
        // is exactly why the broad pin must NOT use it: that one claims a plan CHOICE, and a choice made
        // inside a prohibition is not production's choice (code-reviewer, #875).
        if (disableSeqScan)
        {
            await using var guc = connection.CreateCommand();
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
                // Decorrelated from org.nr order (code-reviewer, #875): a bulk insert in ascending i gives
                // company_name a correlation of ~1.0, which makes the planner price this index's heap
                // fetches as SEQUENTIAL — pricing the very plan we pin at its floor. The real register is
                // upserted in SCB file order (org.nr), so its correlation is ~0. A cheap deterministic
                // shuffle removes the flattery.
                Name = $"Företag {(i * 7919) % SeededRows:D4} AB",
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
