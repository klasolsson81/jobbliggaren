using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
/// <b>#1681 part 2 (ADR 0139) — the AD half of this suite no longer pins the GIN index, because the
/// ad statements no longer read <c>company_register</c> at all.</b> The predicate's expensive half is
/// resolved out of the request path by the materialisation job, so what is left is
/// <c>members ⋈ job_ads</c>. The three ad pins therefore claim the OPPOSITE pair of facts: the
/// member table's PK index is IN the plan, and <c>company_register</c> is NOT.
///
/// <para>
/// <b>That absence assertion needs a positive control, and this file's own docblock explains why</b>
/// (see the "never a negative 'no Seq Scan'" paragraph two above): a <c>ShouldNotContain</c> passes
/// for every reason, including the ones that mean the instrument is broken — an EXPLAIN that failed,
/// a plan that came back empty, a statement that did not run. <c>CompanyQueries_StillReadTheRegister_-
/// SoTheAbsenceAssertionsCanFail</c> is that control: it EXPLAINs a still-register-backed statement
/// through the SAME helper and asserts the token IS present. Without it, deleting the register from
/// the world would turn all three ad pins green.
/// </para>
///
/// <para>
/// <b>Three ad-side predicate pins were RETIRED here rather than migrated, because the property
/// MOVED</b> (CLAUDE.md §5 <c>Tests:</c> — the seam names the pin when it lives elsewhere). The
/// de-registered-company exclusion (DPIA M-D6), the kommun axis and the exact-five-digit SNI match
/// are no longer read-path conjuncts; they are decided when the member set is COMPUTED. They are
/// pinned at their new homes: <c>CompanyWatchCriterionMaterialisationTests.Materialise_-
/// RemovesADeregisteredCompany_OnTheNextRun_TheMD6Replacement</c> and
/// <c>...Materialise_WritesExactlyTheMatchingActiveCompanies_AndAnHonestState</c> (which seeds a
/// wrong-kommun company), plus <c>CompanyWatchBrowseQueryTests.Browse_MatchesExactFiveDigitSniCodes_-
/// NeverAPrefix</c> and <c>...Browse_NeverReturnsADeregisteredCompany</c> — the candidate selection
/// shares <c>CompanyWatchBrowseQuery.FromWhere</c> and <c>BindPredicate</c> with the register browse
/// those two pin, so it is one predicate with one set of pins rather than two copies.
/// </para>
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
    /// <summary>#1681 part 2 — an age bound far enough back that the fixture's freshly
    /// written row always passes it. The EXPLAIN pins claim a STATEMENT and its plan, not the
    /// age gate; the gate's own arms are measured by
    /// <see cref="AdQueries_ReportNotMaterialised_WhenTheRowIsOlderThanTheReadAgeBound"/> and
    /// <see cref="AdQueries_ReportNotMaterialised_WhenAnOverAgeRowIsTooBroad_NeverTheStaleRefusal"/>,
    /// which drive the port at its shipped default instead.</summary>
    private static readonly DateTimeOffset FreshEnough =
        DateTimeOffset.UtcNow.AddDays(-1);

    /// <summary>
    /// #1681 part 2 — the read-side age bound, taken from the option's OWN default rather than
    /// transcribed as a 72. <see cref="PortFor"/> builds the port from that same default, so the
    /// gate and the fixture that has to clear it move together; a bound re-derived against a new
    /// cadence cannot leave a stale literal behind here.
    /// </summary>
    private static readonly int ReadAgeHours =
        new CompanyWatchMaterialisationOptions().MaxReadAgeHours;

    private readonly WorkerTestFixture _fixture = fixture;

    private const string GinIndexName = "ix_company_register_sni_codes_gin";
    private const string OverlapOperator = "&&";

    // #1681 part 2 — the member table's PRIMARY KEY, (criterion_id, organization_number). It is what
    // makes "the org.nr this criterion matched" an index-only lookup, and it is the plan node the
    // breadth-gate bound was DERIVED against (Index Only Scan, Heap Fetches: 0).
    private const string MemberPkIndexName = "pk_company_watch_criterion_members";

    // The token whose ABSENCE the ad pins assert. It is the relation name, so it appears in a plan
    // whether the register is reached by seq scan, bitmap or index.
    private const string RegisterTable = "company_register";

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

    // #1559 — the ad pins. AdOrgNr is the FIRST probe-SNI company the register seed produces
    // ($"552{i:D7}" at i = 0), so the ads below join a row the criterion genuinely matches.
    //
    // #1681 part 2 — the seed's org.nr shape is now "552…" rather than "550…", and that is load-
    // bearing rather than cosmetic. The materialisation writes members through
    // CompanyWatchCriterionMemberFilter, whose personnummer-shape guard is
    // OrganizationNumber.IsPersonnummerShaped() — third digit < '2'. Under the old shape EVERY
    // seeded company was pnr-shaped, so every member set would have come back empty and every pin
    // below would have measured an empty fixture rather than a plan.
    private const string AdOrgNr = "5520000000";
    private const int AdRows = 5;

    // Enough job_ads that driving the join from THAT side is no longer the cheapest plan — see the
    // filler-ads comment in SeededContextWithAdsAsync for why a small seed pins the opposite of
    // production. The semantic pins deliberately do NOT pay for this: they assert rows, not plans.
    private const int PlanRegimeAds = 20_000;

    // A kommun no seeded row sits in. It used to make the read path's kommun conjunct measurable;
    // since #1681 part 2 that conjunct lives in the materialisation, and this constant's job is to
    // give the staleness pin a genuinely DIFFERENT predicate to edit the criterion to.
    private const string OtherKommun = "1480";

    // #1681 part 2 — the digest of the criterion the fixture materialises. Computed the way
    // production computes it, from a spec built by Create (not FromTrusted), so the value here and
    // the value the materialiser stamped are the same by construction rather than by transcription.
    private static readonly CriteriaFingerprint ProbeFingerprint = CriteriaFingerprint.Of(
        CompanyWatchCriteriaSpec.Create([ProbeSni], [SeededKommun]).Value);

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
        + "ORDER BY company_name precisely because that sort INHERITS the same collation. Write an "
        + "explicit COLLATE into the ORDER BY and it stops inheriting: the sort is then requested under "
        + "whatever you named, the index was not built under that, and Postgres does not error — it "
        + "silently Sorts the whole match set. Note the trap precisely: COLLATE \"swedish\" is HARMLESS "
        + "(same collation OID; Postgres strips the redundant CollateExpr and the plan is unchanged). "
        + "The one that kills it is a DIFFERENT collation object — including one with the SAME LOCALE, "
        + "e.g. the built-in `sv-SE-x-icu`, which sorts identically and yet is a different OID the "
        + "planner will not match against this index. So the rule is not 'name the right collation', it "
        + "is: DO NOT WRITE COLLATE IN THIS QUERY AT ALL. The column carries it — that is the whole "
        + "point of putting it there. "
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

    [Fact]
    public async Task MagnitudeQuery_UsesTheSniGinIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextAsync(ct);

        // #560 PR-3 (Fork G3) — the magnitude command reuses CountSql VERBATIM (only the bound cap
        // differs), so today this pin is the count pin by transitivity. It exists SEPARATELY
        // because that reuse is one refactor away from being false: give the magnitude its own SQL
        // text and, without this pin, its plan is free to regress into a full scan while the count
        // pin stays green — every headline and every picker preview then walks 1,17M rows.
        var plan = await ExplainAsync(
            ctx.Db,
            (conn, spec) => CompanyWatchBrowseQuery.BuildMagnitudeCommand(conn, spec, ceiling: 10_000),
            ct);

        AssertServedByGin(plan, "magnitude");
    }

    [Fact]
    public async Task AdIdsQuery_ReadsTheMaterialisedMemberSet_AndNeverTheRegister()
    {
        // #1681 part 2 (ADR 0139) — THE pin for what the whole change buys, expressed as a plan.
        // Before it, this statement joined job_ads onto a 1,07M-row register scan inside a request on
        // the 300 ms budget; after it, the register is not in the plan at all and what remains is an
        // index lookup against a pre-computed, breadth-gated org.nr set.
        //
        // The negative half is what the ADR claims and the positive half is what makes the claim
        // measurable — see AssertReadsTheMemberSet, and see
        // CompanyQueries_StillReadTheRegister_SoTheAbsenceAssertionsCanFail for the control that
        // proves the absence assertion is capable of failing at all.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct, fillerAds: PlanRegimeAds);

        var plan = await ExplainMaterialisedAsync(
            ctx,
            (conn, id, fp) => CompanyWatchBrowseQuery.BuildAdIdsCommand(conn, id, page: 1, pageSize: 20),
            ct);

        AssertReadsTheMemberSet(plan, "ad ids");
    }

    [Fact]
    public async Task AdCountQuery_ReadsTheMaterialisedMemberSet_AndNeverTheRegister()
    {
        // The ad count is its own statement (a state row driving a capped subquery), serving BOTH the
        // pagination cap and the headline magnitude. Pinning only the id query would leave the number
        // free to regress back onto the register while the list stayed fast — and the number is the
        // thing every criterion row renders, whether or not anyone opens the list.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct, fillerAds: PlanRegimeAds);

        var plan = await ExplainMaterialisedAsync(
            ctx,
            (conn, id, fp) => CompanyWatchBrowseQuery.BuildAdCountCommand(conn, id, fp, FreshEnough, cap: 10_000),
            ct);

        AssertReadsTheMemberSet(plan, "ad count");
    }

    [Fact]
    public async Task AdIdSetQuery_ReadsTheMaterialisedMemberSet_AndNeverTheRegister()
    {
        // #1656 (b) — a THIRD command text, therefore a THIRD plan, and the one whose cost story the
        // grading bound rests on. It differs from the ad-id page query in the two ways a planner cares
        // about: no OFFSET, and a LIMIT three orders of magnitude larger. It also wraps the whole
        // thing in a LEFT JOIN LATERAL, which is exactly where this codebase has been burned before
        // (ADR 0139 rejected a batched form whose lateral sat over a jsonb_to_recordset function scan:
        // no statistics, no index lookup, 40-50x). The other two pins cannot see any of that.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct, fillerAds: PlanRegimeAds);

        var plan = await ExplainMaterialisedAsync(
            ctx,
            (conn, id, fp) => CompanyWatchBrowseQuery.BuildAdIdSetCommand(
                conn, id, fp, FreshEnough, maxSetSize: CriterionMatchingAdSetResolver.MaxSetSize),
            ct);

        AssertReadsTheMemberSet(plan, "ad id set");
    }

    [Fact]
    public async Task CompanyQueries_StillReadTheRegister_SoTheAbsenceAssertionsCanFail()
    {
        // THE POSITIVE CONTROL, and it is not optional — this file's own docblock is written against
        // exactly this failure mode one axis over ("a ShouldNotContain('Seq Scan') would PASS under
        // the mutation"). An assertion that `company_register` is ABSENT from a plan passes for every
        // reason there is: the statement changed, the EXPLAIN returned nothing, the helper broke, the
        // fixture is empty. Without a statement that DOES carry the token, through the SAME helper,
        // on the SAME connection, the three ad pins above would go green if the register ceased to
        // exist.
        //
        // The company half is the control because it is STILL register-backed by decision, not by
        // accident (senior-cto-advisor 2026-09-06, Decision 5): the member table stores only
        // (criterion_id, organization_number) while the browse projects company_name, kommun and
        // sni_codes, and CountMatchingCompaniesAsync additionally serves an UNSAVED criterion the
        // picker previews. So this control is also a pin on that decision: the day someone moves the
        // company half onto members, this test is what says so.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct, fillerAds: PlanRegimeAds);

        var itemsPlan = await ExplainAsync(
            ctx.Db,
            (conn, spec) => CompanyWatchBrowseQuery.BuildItemsCommand(conn, spec, page: 1, pageSize: 20),
            ct);

        itemsPlan.ShouldContain(
            RegisterTable,
            customMessage:
                "The company ITEMS plan no longer names " + RegisterTable + ". Either the company "
                + "half stopped reading the register — which is a decision, not a refactor (CTO "
                + $"2026-09-06 Decision 5) — or this instrument is broken, in which case the three "
                + $"ad-side absence pins are currently vacuous.{Environment.NewLine}"
                + $"Plan:{Environment.NewLine}{itemsPlan}");

        var countPlan = await ExplainAsync(
            ctx.Db,
            (conn, spec) => CompanyWatchBrowseQuery.BuildCountCommand(conn, spec, pageSize: 20),
            ct);

        countPlan.ShouldContain(
            RegisterTable,
            customMessage:
                $"The company COUNT plan no longer names {RegisterTable}. See the items assertion "
                + $"above.{Environment.NewLine}Plan:{Environment.NewLine}{countPlan}");
    }

    [Fact]
    public void AdIdSetQuery_OrdersByATotalKey()
    {
        // The set query and the page query share AdsOrderBy, and this is what that sharing is FOR:
        // the filtered view paginates the SET while the unfiltered view paginates the PAGE query, so
        // two different orders would sequence one against the other. Asserted on the SQL rather than
        // the plan for the same reason its sibling is -- and because a MISSING order cannot be seen
        // behaviourally here: the fixture inserts newest-first, so heap order and published_at DESC
        // coincide.
        using var conn = new NpgsqlConnection();
        using var cmd = CompanyWatchBrowseQuery.BuildAdIdSetCommand(
            conn, CompanyWatchCriterionId.New(), ProbeFingerprint, FreshEnough,
            maxSetSize: CriterionMatchingAdSetResolver.MaxSetSize);

        cmd.CommandText.ShouldContain(
            "ORDER BY j.published_at DESC, j.id",
            customMessage:
                "The ad-set query's ORDER BY is no longer TOTAL, or no longer shared with the page "
                + "query. The filtered view cuts its page from THIS sequence while the unfiltered view "
                + "pages the other statement; two orders means one is sequenced against the other.");

        // dotnet-architect, 2026-09-06 — the INNER order (above) only decides which rows the
        // lateral's LIMIT keeps. What the CALLER observes, and what the port's docblock names as the
        // contract, is the OUTER order; deleting that clause left the assertion above green while the
        // published order became a property of the plan shape (a Nested Loop happens to preserve the
        // inner order) rather than of the statement. That is precisely what the port's own docblock
        // refuses to rely on, so both clauses are pinned.
        cmd.CommandText.ShouldContain(
            "ORDER BY a.published_at DESC, a.id",
            customMessage:
                "The whole-set query no longer publishes its OUTER order. The inner ORDER BY only "
                + "cuts the LIMIT; the outer one is the order CriterionMatchingAdSetResolver "
                + "filters against to produce both the ad sequence and the count's destination.");
    }

    [Fact]
    public void AdIdsQuery_OrdersByATotalKey()
    {
        // Same guarantee as ItemsQuery_OrdersByATotalKey, same reason it is asserted on the SQL rather
        // than the plan: published_at is FAR from unique here (a bulk ingest stamps a whole batch
        // identically), Postgres sorts are not stable, and an OFFSET walk over a non-total order
        // silently drops and duplicates ads ACROSS pages. j.id is the PK — it is what makes it total.
        //
        // It is also a CONTRACT, not just an internal property: BrowseCriterionAdsQueryHandler loads
        // the ids and RE-STATES this order (`WHERE id = ANY(...)` does not preserve array order), so a
        // change here silently paginates against one order while rendering another.
        using var conn = new NpgsqlConnection();
        using var cmd = CompanyWatchBrowseQuery.BuildAdIdsCommand(
            conn, CompanyWatchCriterionId.New(), page: 1, pageSize: 20);

        cmd.CommandText.ShouldContain(
            "ORDER BY j.published_at DESC, j.id",
            customMessage:
                "The ad query's ORDER BY is no longer TOTAL. published_at is not unique (a bulk ingest "
                + "stamps a batch identically) and Postgres sorts are not stable, so an OFFSET walk "
                + "over a non-total order silently drops and duplicates ads across pages. Keep j.id "
                + "(the PK) as the tiebreak — and remember BrowseCriterionAdsQueryHandler re-states "
                + "this exact order when it loads the rows.");
    }

    [Fact]
    public async Task AdQueries_CountAnAdOnce_EvenWhenSeveralOfTheCriterionsSniCodesMatchItsCompany()
    {
        // The set cannot double-count, and #1681 part 2 changed WHY. It used to rest on
        // company_register.organization_number being the register's PRIMARY KEY, so `sni_codes && @sni`
        // could not fan the join out. The register is gone from this path; the guarantee now rests on
        // company_watch_criterion_members' PK (criterion_id, organization_number), which names each
        // org.nr at most once per criterion — so `= ANY(that set)` matches each ad exactly once.
        //
        // The hazard is therefore absent BY CONSTRUCTION rather than avoided, but the fixture is kept
        // adversarial anyway: the probe company carries BOTH of the criterion's SNI codes, so a
        // materialisation that emitted one member row per matching code would inflate every number on
        // this surface by a factor nobody could see from the copy.
        //
        // This is a SEMANTIC pin, so it EXECUTES rather than EXPLAINs.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        var count = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct);
        var page = await port.BrowseAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, 1, 20, ct);

        // AdRows ads exist in total, all Active, all at a company this criterion matched.
        count.State.ShouldBe(CriterionMaterialisationState.Materialised);
        count.Count.ShouldBe(AdRows);

        page.State.ShouldBe(CriterionMaterialisationState.Materialised);
        page.Page!.Items.Count.ShouldBe(AdRows);
        page.Page.Items.Distinct().Count().ShouldBe(AdRows);
    }

    [Fact]
    public async Task AdQueries_ExcludeArchivedAds()
    {
        // `j.status = @ad_status` is the WHOLE ad-side exclusion — JobAd has no soft-delete axis and no
        // query filter (#821). Drop that conjunct and a retracted ad reappears in both the count and
        // the list, with every plan pin in this file still green (an EXPLAIN cannot see which rows
        // come back).
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        // BASELINE, measured here rather than inherited from a sibling test: without it a fixture
        // whose materialisation had produced no members would read 0 before and 0 after, and the pin
        // would be vacuously green (test-writer V5).
        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);

        // Parameterised, and the status comes from the SmartEnum rather than a hand-typed literal:
        // a renamed member must break the build, not silently exclude the row for being garbage.
        await ctx.Db.Database.ExecuteSqlRawAsync(
            "UPDATE job_ads SET status = {0} WHERE organization_number = {1};",
            [JobAdStatus.Archived.Value, AdOrgNr], ct);

        // ZERO, and emphatically a MATERIALISED zero: the criterion is still materialised, its member
        // set is unchanged, and the honest answer is that those companies have no active ad right now.
        // A refusal or an absent materialisation here would say something else entirely.
        var after = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct);
        after.State.ShouldBe(CriterionMaterialisationState.Materialised);
        after.Count.ShouldBe(0);

        var page = await port.BrowseAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, 1, 20, ct);
        page.State.ShouldBe(CriterionMaterialisationState.Materialised);
        page.Page!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AdCount_SaturatesAtTheCallersCeiling_NeverReportsMore()
    {
        // The handler test proves the DTO carries the flag; only this one proves the SQL can produce
        // the capped number at all, and that the PORT sets Saturated from the cap it actually applied
        // (#1681 part 2 moved that decision here from the resolver, which used to recompute it).
        // Delete `LIMIT @count_cap` from the count statement and this is the test that notices.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        var capped = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows - 2, ct);
        capped.Count.ShouldBe(AdRows - 2);
        capped.Saturated.ShouldBeTrue();

        var roomy = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct);
        roomy.Count.ShouldBe(AdRows);
        roomy.Saturated.ShouldBeFalse();
    }

    [Fact]
    public async Task AdBrowse_TotalCountCanNeverAdvertiseAPageTheValidatorWouldReject()
    {
        // `TotalPages = ceil(TotalCount / PageSize)` while the validator 400s past MaxPage, so the
        // count MUST cap at MaxPage x PageSize — CompanyBrowseCriteria calls that a CORRECTNESS
        // requirement, not a perf tweak, and on the ad path nothing else measures it (test-writer L2).
        // pageSize 1 makes the cap reachable with a seed this suite can afford.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(
            ct, fillerAds: 0, probeAds: CompanyBrowseCriteria.MaxPage + 5);

        var port = PortFor(ctx.Db);

        var page = await port.BrowseAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, 1, 1, ct);

        page.State.ShouldBe(CriterionMaterialisationState.Materialised);
        page.Page!.TotalCount.ShouldBe(CompanyBrowseCriteria.MaxServableRows(1));
        page.Page.TotalPages.ShouldBe(CompanyBrowseCriteria.MaxPage);
    }

    [Fact]
    public async Task ListActiveAdIds_ReturnsTheWholeSet_WhenItFitsTheBoundExactly()
    {
        // #1656 (b) — the boundary is asserted at EXACTLY the bound, not comfortably inside it. The
        // statement asks for `LIMIT maxSetSize + 1` and the reader refuses on the extra row, so an
        // off-by-one in either place is a set of AdRows that comes back refused (or, worse, a set of
        // AdRows - 1 that comes back looking complete).
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        var ids = await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows, ct);

        ids.Refused.ShouldBeFalse();
        ids.State.ShouldBe(CriterionMaterialisationState.Materialised);
        ids.Ids!.Count.ShouldBe(AdRows);
        ids.Ids.Distinct().Count().ShouldBe(AdRows);
    }

    [Fact]
    public async Task ListActiveAdIds_RefusesTheWholeSet_RatherThanReturningAPrefix()
    {
        // The property the count's honesty rests on. A prefix here would be graded and counted, and
        // the resulting number would be a FLOOR rendered as an exact figure -- which is the defect
        // the refusal bound exists to prevent, and which no other test in this repo could see.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        // One under the true size: the set does not fit.
        var refused = await port.ListActiveAdIdsAsync(
            ctx.CriterionId, ctx.Fingerprint, AdRows - 1, ct);
        refused.Refused.ShouldBeTrue();
        refused.Ids.ShouldBeNull();

        // ...and the refusal is THIS QUESTION's, not the criterion's: the materialisation stands.
        // Reading the refusal as a company-level breadth refusal would send the user to narrow a watch that
        // the breadth gate accepted.
        refused.State.ShouldBe(CriterionMaterialisationState.Materialised);

        // And a bound that comfortably fits still returns everything -- so the refusal above is the
        // bound and not a fixture that stopped matching the criterion (test-writer V5).
        var roomy = await port.ListActiveAdIdsAsync(
            ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct);
        roomy.Refused.ShouldBeFalse();
        roomy.Ids!.Count.ShouldBe(AdRows);
    }

    [Fact]
    public async Task ListActiveAdIds_PublishesTheSameTotalOrderAsThePageQuery()
    {
        // Both statements share MaterialisedAdsOrderBy, and this is what that sharing is FOR: the
        // filtered view paginates the set while the unfiltered view paginates the page query. Two
        // different orders would sequence one against the other, which is the trap
        // BrowseCriterionAdsQueryHandler already documents one step downstream.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        var set = await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct);
        var page = await port.BrowseAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, 1, AdRows, ct);

        set.Ids.ShouldNotBeNull();
        set.Ids.Count.ShouldBe(AdRows);
        set.Ids.ShouldBe(page.Page!.Items);
    }

    [Fact]
    public async Task ListActiveAdIds_ExcludesArchivedAds_LikeItsSiblings()
    {
        // The set inherits the SAME `j.status = @ad_status` conjunct as the count and the page,
        // because it carries the same MaterialisedAdsFromWhere. Asserted rather than assumed: this set
        // is what gets GRADED, so an archived ad slipping in would be counted as a match and rendered.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        (await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct))
            .Ids!.Count.ShouldBe(AdRows);

        await ctx.Db.Database.ExecuteSqlRawAsync(
            "UPDATE job_ads SET status = {0} WHERE organization_number = {1};",
            [JobAdStatus.Archived.Value, AdOrgNr], ct);

        // Empty, and emphatically NOT a refusal and NOT an absent materialisation: an empty set is
        // "no ads match this criterion", while either of the other two claims we cannot say.
        var afterArchive = await port.ListActiveAdIdsAsync(
            ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct);
        afterArchive.State.ShouldBe(CriterionMaterialisationState.Materialised);
        afterArchive.Refused.ShouldBeFalse();
        afterArchive.Ids.ShouldNotBeNull();
        afterArchive.Ids.ShouldBeEmpty();
    }

    [Fact]
    public async Task AdQueries_ReportNotMaterialised_WhenNoRunHasStampedTheCriterion()
    {
        // #1681 part 2 — the state EVERY criterion is in between its creation and the next run, and
        // the one all three statements answer from the absence of a state row rather than from the
        // absence of members. It must not read as a zero: "we have not counted this yet" and "these
        // companies have no ads" are different facts and the surfaces render different copy.
        //
        // The criterion is created through the aggregate's own factory (the production create path)
        // and the job is deliberately NOT run — no state row is hand-written here.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var fresh = await SeedCriterionAsync([ProbeSni], [SeededKommun], ct);
        var port = PortFor(ctx.Db);

        (await port.CountActiveAdsAsync(fresh, ProbeFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        (await port.ListActiveAdIdsAsync(fresh, ProbeFingerprint, 1_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        (await port.BrowseAdIdsAsync(fresh, ProbeFingerprint, 1, 20, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);

        // The negative control: the SAME statements, on the SAME data, answer with a number for the
        // criterion the job DID materialise. Without it this test would also pass against an empty
        // job_ads table, or a broken join.
        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);
    }

    [Fact]
    public async Task AdQueries_ReportTooBroad_WhenTheBreadthGateRefusedTheCriterion()
    {
        // The refusal, end to end. The breadth gate stores NO members and a TooBroad state row, so the
        // three statements must answer with a refusal rather than the honest zero an empty member set
        // would otherwise produce — which is precisely the dishonest zero ADR 0139 wrote the state
        // table for.
        //
        // The broad criterion is [ProbeSni, FillerSni] x [SeededKommun], which matches every one of
        // the SeededRows companies — over CompanyWatchCriterionMember.MaxPerCriterion.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var broadSpec = CompanyWatchCriteriaSpec.Create(
            [ProbeSni, FillerSni], [SeededKommun]).Value;
        SeededRows.ShouldBeGreaterThan(CompanyWatchCriterionMember.MaxPerCriterion,
            "fixturen måste vara bredare än grinden, annars mäter testet ingen vägran");

        var broad = await SeedCriterionAsync([ProbeSni, FillerSni], [SeededKommun], ct);
        await RunMaterialiserAsync(ct);

        var port = PortFor(ctx.Db);
        var fingerprint = CriteriaFingerprint.Of(broadSpec);

        (await port.CountActiveAdsAsync(broad, fingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);
        (await port.ListActiveAdIdsAsync(broad, fingerprint, 1_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);
        (await port.BrowseAdIdsAsync(broad, fingerprint, 1, 20, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);

        // ...and TooBroad is not NotMaterialised: the row EXISTS and says why.
        (await port.CountActiveAdsAsync(broad, fingerprint, 10_000, ct))
            .State.ShouldNotBe(CriterionMaterialisationState.NotMaterialised);
    }

    [Fact]
    public async Task AdQueries_ReportNotMaterialised_AfterThePredicateIsEdited_NeverTheOldNumber()
    {
        // THE STALENESS GUARD, end to end, and the whole reason the discriminator is a FINGERPRINT.
        // The member set left behind by a run is exact for a predicate its owner no longer has, so
        // rendering its number would not be stale — it would be FALSE.
        //
        // The edit goes through CompanyWatchCriterion.UpdateCriteria, the production edit path, and
        // the job is deliberately not re-run: what is measured is what a read sees in the window
        // between the save and the next materialisation, which is a window every edit passes through.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        // BASELINE: before the edit the criterion answers with a real number.
        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);

        var edited = CompanyWatchCriteriaSpec.Create([ProbeSni], [OtherKommun]).Value;
        await UpdateCriteriaAsync(ctx.CriterionId, edited, ct);
        var editedFingerprint = CriteriaFingerprint.Of(edited);

        // The read asks about the criterion AS IT IS NOW, and the stored row was written for the
        // predicate it had BEFORE. All three statements report ignorance.
        (await port.CountActiveAdsAsync(ctx.CriterionId, editedFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        (await port.ListActiveAdIdsAsync(ctx.CriterionId, editedFingerprint, 1_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        (await port.BrowseAdIdsAsync(ctx.CriterionId, editedFingerprint, 1, 20, ct))
            .State.ShouldBe(CriterionMaterialisationState.NotMaterialised);

        // The row is still there, and it still carries the OLD member set — which is exactly what
        // makes the guard load-bearing rather than decorative: without it, that set would answer.
        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);

        // ...and the next run heals it.
        await RunMaterialiserAsync(ct);
        (await port.CountActiveAdsAsync(ctx.CriterionId, editedFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.Materialised);
    }

    [Fact]
    public async Task AdQueries_KeepTheirNumbers_AcrossAPureRename()
    {
        // The OTHER arm of the same guard, and the reason it is not a timestamp. Rename bumps
        // UpdatedAt exactly as UpdateCriteria does, so an "is the row newer than the materialisation"
        // guard would blank a working watch's numbers because its owner renamed it — a false refusal
        // on the most common edit, and one that would repeat on every read until the next nightly run.
        //
        // Without this arm the fingerprint could be replaced by a timestamp comparison and every other
        // test in this file would stay green.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);

        var updatedAtBefore = await RenameCriterionAsync(ctx.CriterionId, "Nytt namn", ct);

        // The rename really did move the row's mtime — otherwise this test would prove nothing about
        // the timestamp guard it exists to rule out.
        updatedAtBefore.Before.ShouldBeLessThan(updatedAtBefore.After);

        // ...and the numbers are untouched, because the PREDICATE is untouched.
        var after = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct);
        after.State.ShouldBe(CriterionMaterialisationState.Materialised);
        after.Count.ShouldBe(AdRows);

        (await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct))
            .Ids!.Count.ShouldBe(AdRows);
    }

    [Fact]
    public async Task AdQueries_ReportNotMaterialised_WhenTheRowIsOlderThanTheReadAgeBound()
    {
        // #1681 part 2 — THE age gate, and the first test that has ever seen it fire.
        //
        // The bound is enforced TWICE on purpose: in SQL (so an over-age row costs no ad scan) and in
        // C#, which is what DECIDES. The SQL half alone does not fail safe, and it failed in both
        // directions at once. The count statement's CASE yields NULL, which the reader turns into
        // InvalidOperationException — a 500 on /me/company-watch-criteria, /{id}/ads and
        // /{id}/ad-count, in the ordinary state the bound was added for. The id-set statement's
        // lateral yields no rows, which the reader reads as an honest empty set — the fabricated zero
        // ADR 0120 and this whole feature exist against, and the one that is silent. Both assertions
        // below fail if the C# arm is removed, which is what makes them a pin rather than a wish.
        //
        // PREMISE (CLAUDE.md §5 Tests:). The over-age row is written by the PRODUCTION materialiser,
        // driven by a clock reading four days back. That is exactly the state src/ produces when a run
        // succeeded then and no later run has — CompanyWatchCriterionMaterialiser stamps
        // clock.UtcNow (pinned by CompanyWatchCriterionMaterialisationTests.Materialise_-
        // StampsMaterialisedAtFromTheClock_AndAdvancesItOnEveryRun), and a row simply ages; its own
        // failure-tolerant loop is what lets one criterion sit there while its siblings advance. No
        // column is hand-written here, and the actor's own output is asserted before anything is
        // claimed about the reader.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        var port = PortFor(ctx.Db);

        // POSITIVE CONTROL, on the SAME criterion and before anything ages. Without it every
        // assertion below would pass for any reason at all — an empty job_ads table, a fingerprint
        // that never matched, a fixture that wrote no state row.
        (await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct))
            .Count.ShouldBe(AdRows);
        (await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct))
            .Ids!.Count.ShouldBe(AdRows);

        // The same job, one whole cadence period past the bound.
        await RunMaterialiserAsAtAsync(DateTimeOffset.UtcNow.AddHours(-(ReadAgeHours + 24)), ct);

        // The actor's own transform admits the state, and AGE is provably the only disqualifier the
        // assertions below can be reading: the row is still Materialised and still carries the
        // criterion's CURRENT fingerprint, so neither of the other two NotMaterialised triggers is
        // available to produce these answers.
        var row = await ReadMaterialisationAsync(ctx.CriterionId, ct);
        row.ShouldNotBeNull();
        row!.State.ShouldBe(MaterialisationState.Materialised.ToString());
        row.CriteriaFingerprint.ShouldBe(ctx.Fingerprint.Value);
        row.MaterialisedAt.ShouldBeLessThan(
            DateTimeOffset.UtcNow.AddHours(-ReadAgeHours),
            "the fixture must actually be over-age, or this test measures nothing at all");

        // THE COUNT READER MUST NOT THROW. Under the SQL gate alone the CASE is NULL here and the
        // reader raises "gates no longer agree" — a 500 for a user whose watch merely went stale.
        // Captured out of the lambda rather than re-read after it: a second call would be a second
        // measurement, and this whole family exists against those.
        MaterialisedAdCount counted = null!;
        await Should.NotThrowAsync(async () =>
            counted = await port.CountActiveAdsAsync(ctx.CriterionId, ctx.Fingerprint, 10_000, ct));
        counted.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        counted.Count.ShouldBeNull();

        // THE ID-SET READER MUST NOT RETURN Resolved([]). An empty list under Materialised is an
        // honest "this criterion matches no active ad right now" — a claim nobody has been in a
        // position to make for three cadence periods. Ids is non-null exactly under Materialised (the
        // record's own constructor enforces that), so the two assertions are one claim from two
        // directions rather than a repetition.
        var ids = await port.ListActiveAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, AdRows + 100, ct);
        ids.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        ids.Ids.ShouldBeNull(
            "an over-age row must not answer with an empty set — that is a number, and it is invented");
        ids.Refused.ShouldBeFalse();

        // The browse route reaches the same reader, so it carries the same 500 under the mutation.
        MaterialisedAdPage page = null!;
        await Should.NotThrowAsync(async () =>
            page = await port.BrowseAdIdsAsync(ctx.CriterionId, ctx.Fingerprint, 1, 20, ct));
        page.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        page.Page.ShouldBeNull();
    }

    [Fact]
    public async Task AdQueries_ReportNotMaterialised_WhenAnOverAgeRowIsTooBroad_NeverTheStaleRefusal()
    {
        // The age comparison sits BEFORE the TooBroad branch, deliberately, and this is the only test
        // that can see that ordering. A refusal nobody has re-checked for three cadence periods is not
        // a refusal we still stand behind: it tells the user to narrow a watch on the strength of a
        // company set that may have shrunk under her three days ago. Moving the age check below the
        // TooBroad branch keeps every other test in this file green.
        //
        // PREMISE: as above — the production materialiser refuses the criterion at the breadth gate,
        // and the run that did so is dated four days back by the injected clock.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        SeededRows.ShouldBeGreaterThan(CompanyWatchCriterionMember.MaxPerCriterion,
            "fixturen måste vara bredare än grinden, annars mäter testet ingen vägran");

        var broadSpec = CompanyWatchCriteriaSpec.Create([ProbeSni, FillerSni], [SeededKommun]).Value;
        var broadFingerprint = CriteriaFingerprint.Of(broadSpec);
        var broad = await SeedCriterionAsync([ProbeSni, FillerSni], [SeededKommun], ct);

        var port = PortFor(ctx.Db);

        // POSITIVE CONTROL: fresh, the same criterion answers with a refusal — so the assertions below
        // measure the age gate rather than a criterion that was never refused in the first place.
        await RunMaterialiserAsync(ct);
        (await port.CountActiveAdsAsync(broad, broadFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);

        await RunMaterialiserAsAtAsync(DateTimeOffset.UtcNow.AddHours(-(ReadAgeHours + 24)), ct);

        // The stored row still SAYS TooBroad and still carries the current fingerprint. Only its age
        // changed, which is what makes the three answers below attributable to the age arm alone.
        var row = await ReadMaterialisationAsync(broad, ct);
        row.ShouldNotBeNull();
        row!.State.ShouldBe(MaterialisationState.TooBroad.ToString());
        row.CriteriaFingerprint.ShouldBe(broadFingerprint.Value);
        row.MaterialisedAt.ShouldBeLessThan(DateTimeOffset.UtcNow.AddHours(-ReadAgeHours));

        var counted = await port.CountActiveAdsAsync(broad, broadFingerprint, 10_000, ct);
        counted.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        counted.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);

        var ids = await port.ListActiveAdIdsAsync(broad, broadFingerprint, 1_000, ct);
        ids.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        ids.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);

        var page = await port.BrowseAdIdsAsync(broad, broadFingerprint, 1, 20, ct);
        page.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        page.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);
    }

    [Fact]
    public async Task AdQueries_ReportNotMaterialised_AfterATooBroadWatchIsNarrowed_NeverTheOldRefusal()
    {
        // db-migration-writer, 2026-09-06: the readers used to check STATE before the FINGERPRINT, so
        // a user who narrowed a too-broad watch kept being told it was too broad until the next
        // nightly run — on the one edit the refusal's own copy tells her to make. The fix is the
        // order (fingerprint first), and the write side stamps the fingerprint on the TooBroad path
        // precisely so this read can see the edit.
        //
        // The arm was still untested: AdQueries_ReportTooBroad_... passes a MATCHING fingerprint, and
        // AdQueries_ReportNotMaterialised_AfterThePredicateIsEdited_... starts from a Materialised
        // baseline, so neither can reach a stale fingerprint on a REFUSED row. Hoisting the TooBroad
        // branch back above the fingerprint comparison leaves both of them green.
        //
        // PREMISE: the refusal is written by the production materialiser at the breadth gate, and the
        // edit goes through CompanyWatchCriterion.UpdateCriteria — the same call the update handler
        // makes. What is measured is the window between that save and the next run, which every edit
        // passes through.
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = await SeededContextWithAdsAsync(ct);

        SeededRows.ShouldBeGreaterThan(CompanyWatchCriterionMember.MaxPerCriterion,
            "fixturen måste vara bredare än grinden, annars mäter testet ingen vägran");

        var broadSpec = CompanyWatchCriteriaSpec.Create([ProbeSni, FillerSni], [SeededKommun]).Value;
        var broadFingerprint = CriteriaFingerprint.Of(broadSpec);
        var criterionId = await SeedCriterionAsync([ProbeSni, FillerSni], [SeededKommun], ct);
        await RunMaterialiserAsync(ct);

        var port = PortFor(ctx.Db);

        // BASELINE: refused, and the surfaces render a refusal.
        (await port.CountActiveAdsAsync(criterionId, broadFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);

        // The user does what the refusal told her to do.
        var narrowed = CompanyWatchCriteriaSpec.Create([ProbeSni], [SeededKommun]).Value;
        await UpdateCriteriaAsync(criterionId, narrowed, ct);
        var narrowedFingerprint = CriteriaFingerprint.Of(narrowed);
        narrowedFingerprint.Value.ShouldNotBe(broadFingerprint.Value,
            "the two predicates must digest differently, or this test cannot tell the arms apart");

        // The stored refusal was computed for a predicate she no longer has, so all three statements
        // report ignorance — never the refusal, which would be advice she has already taken.
        var counted = await port.CountActiveAdsAsync(criterionId, narrowedFingerprint, 10_000, ct);
        counted.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        counted.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);

        var ids = await port.ListActiveAdIdsAsync(criterionId, narrowedFingerprint, 1_000, ct);
        ids.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        ids.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);

        var page = await port.BrowseAdIdsAsync(criterionId, narrowedFingerprint, 1, 20, ct);
        page.State.ShouldBe(CriterionMaterialisationState.NotMaterialised);
        page.State.ShouldNotBe(CriterionMaterialisationState.TooBroad);

        // The row is untouched and still says TooBroad for the predicate it was written FOR — which
        // is what makes the guard load-bearing rather than decorative: without the fingerprint
        // comparison, that row is what would answer above.
        (await port.CountActiveAdsAsync(criterionId, broadFingerprint, 10_000, ct))
            .State.ShouldBe(CriterionMaterialisationState.TooBroad);

        // ...and the next run heals it into a real number for the narrowed predicate.
        await RunMaterialiserAsync(ct);
        var healed = await port.CountActiveAdsAsync(criterionId, narrowedFingerprint, 10_000, ct);
        healed.State.ShouldBe(CriterionMaterialisationState.Materialised);
        healed.Count.ShouldBe(AdRows);
    }

    /// <summary>
    /// #1559 — the register seed PLUS job_ads rows that actually join to it, and (#1681 part 2) the
    /// MATERIALISED member set the ad statements now read. All three sides have to be populated: with
    /// an empty <c>job_ads</c> the planner can answer the join from that side alone, and with an empty
    /// member set every ad pin below measures an empty fixture rather than a plan.
    ///
    /// <para>
    /// The ads are attached to the register's FIRST probe-SNI company, and one register row is given
    /// BOTH SNI codes so the count-once pin above has a company that two of the criterion's codes
    /// match.
    /// </para>
    ///
    /// <para>
    /// <b>The member and state rows are written by the PRODUCTION materialiser</b>
    /// (<c>ICompanyWatchCriterionMaterialiser</c>, resolved from the fixture's real graph), never by
    /// this suite — the same discipline <c>CompanyWatchCriterionMaterialisationTests</c> declares. So
    /// the fingerprint, the member set and the state are exactly what a nightly run produces, and no
    /// assertion below rests on a row shape production does not write (CLAUDE.md §5 <c>Tests:</c>).
    /// The criterion itself is created through <c>CompanyWatchCriterion.Create</c>, the same call
    /// <c>CreateCompanyWatchCriterionCommandHandler</c> makes.
    /// </para>
    ///
    /// <para>
    /// <b>The job_ads rows are bulk-inserted, and that is in bounds (CLAUDE.md §5 <c>Tests:</c>).</b>
    /// The state the pins rest on — an Active <c>job_ads</c> row carrying an org.nr, and (in the
    /// archived pins) the same row at <c>Archived</c> — is state <c>src/</c> DOES produce: the
    /// Platsbanken ingest writes the first through <c>JobAd.Import</c>, and
    /// <c>ArchiveExternalJobAdCommandHandler</c> writes the second. The seam is convenience, not a
    /// premise the assertion needs; the register side seeds through the production upsert for the same
    /// reason <see cref="SeededContextAsync"/> does.
    /// </para>
    /// </summary>
    private async Task<ScopedContext> SeededContextWithAdsAsync(
        CancellationToken ct, int fillerAds = 0, int probeAds = AdRows)
    {
        var ctx = await SeededContextAsync(ct);

        // Criteria carry a FK cascade onto both derived tables, so deleting them here clears the
        // member and state rows any earlier test in the serial "Worker" collection left behind. That
        // is what makes the NotMaterialised pin below a measurement rather than a coincidence.
        await ctx.Db.Database.ExecuteSqlRawAsync("DELETE FROM company_watch_criteria;", ct);

        // The probe company gets the second code too — one row, two matching codes.
        await ctx.Db.Database.ExecuteSqlRawAsync(
            "UPDATE company_register SET sni_codes = ARRAY['" + ProbeSni + "','" + FillerSni + "'] "
            + "WHERE organization_number = '" + AdOrgNr + "';", ct);

        await ctx.Db.Database.ExecuteSqlRawAsync("DELETE FROM job_ads;", ct);

        // Ads carrying that org.nr. organization_number is the mapped column JobAdSearchComposition
        // reads, so this is the same key production's ingest writes.
        var seed =
            "INSERT INTO job_ads ("
            + "id, title, company_name, description, url, source, external_source, external_id, "
            + "raw_payload, status, published_at, expires_at, created_at, remote, organization_number) "
            + "SELECT gen_random_uuid(), 'Roll ' || i, 'Probe AB', 'beskrivning', "
            + "'https://example.com/jobs/' || i, 'Platsbanken', 'Platsbanken', 'ext-' || i, "
            + "jsonb_build_object(), 'Active', now() - (i || ' days')::interval, "
            + "now() + interval '60 days', "
            + "now(), false, '" + AdOrgNr + "' "
            + "FROM generate_series(1, " + probeAds + ") AS i;";
        await ctx.Db.Database.ExecuteSqlRawAsync(seed, ct);

        // FILLER ADS — the difference between a pin and an artefact of the test's own smallness.
        // With a handful of ads the planner drives from job_ads and reaches the register by PK, never
        // touching the GIN index: correct at that scale, and the exact OPPOSITE of what production
        // does. Measured 2026-09-04 against the dev stack (register 1 066 938 rows, job_ads 41 597
        // Active), both regimes reach the register through the GIN index — a selective criterion
        // drives FROM it, a broad one has it bitmap-scanned and hashed. Filler ads restore the size
        // ratio that makes a job_ads-driven plan unattractive. The generic-plan pin above learned this
        // same lesson the same way; see its comment.
        if (fillerAds > 0)
        {
            var filler =
                "INSERT INTO job_ads ("
                + "id, title, company_name, description, url, source, external_source, external_id, "
                + "raw_payload, status, published_at, expires_at, created_at, remote, organization_number) "
                + "SELECT gen_random_uuid(), 'Filler ' || i, 'Filler AB', 'beskrivning', "
                + "'https://example.com/filler/' || i, 'Platsbanken', 'Platsbanken', 'filler-' || i, "
                + "jsonb_build_object(), 'Active', now() - (i % 200 || ' days')::interval, "
                + "now() + interval '60 days', now(), false, lpad((900000 + i)::text, 10, '0') "
                + "FROM generate_series(1, " + fillerAds + ") AS i;";
            await ctx.Db.Database.ExecuteSqlRawAsync(filler, ct);
        }

        await ctx.Db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
        await ctx.Db.Database.ExecuteSqlRawAsync("ANALYZE company_register;", ct);

        // #1681 part 2 — the criterion the ad statements are keyed on, and the member set the
        // production job resolves for it. Run LAST, so the candidate selection plans against the
        // statistics the two ANALYZEs above just refreshed; the run's own ANALYZE then covers the two
        // materialisation tables, which is what the member-PK plan pins need.
        ctx.CriterionId = await SeedCriterionAsync([ProbeSni], [SeededKommun], ct);
        ctx.Fingerprint = ProbeFingerprint;
        await RunMaterialiserAsync(ct);

        return ctx;
    }

    /// <summary>
    /// Creates a criterion through the aggregate's own factory — the same call
    /// <c>CreateCompanyWatchCriterionCommandHandler</c> makes — and saves it. A fresh user id per
    /// criterion, so nothing here depends on the per-user cap.
    /// </summary>
    private async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        string[] sni, string[] kommun, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var spec = CompanyWatchCriteriaSpec.Create(sni, kommun);
        spec.IsSuccess.ShouldBeTrue("seed: specen måste vara giltig");

        var criterion = CompanyWatchCriterion.Create(
            Guid.NewGuid(), spec.Value, null, new FixedClock(T0));
        criterion.IsSuccess.ShouldBeTrue("seed: kriteriet måste kunna skapas");

        db.CompanyWatchCriteria.Add(criterion.Value);
        await db.SaveChangesAsync(ct);

        return criterion.Value.Id;
    }

    /// <summary>The production materialisation job, from the fixture's real graph.</summary>
    private async Task RunMaterialiserAsync(CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = scope.ServiceProvider
            .GetRequiredService<ICompanyWatchCriterionMaterialiser>();
        await materialiser.MaterialiseAsync(ct);
    }

    /// <summary>
    /// The SAME production materialiser, driven by a clock reading an instant in the past.
    ///
    /// <para>
    /// <b>This is not a hand-written row and not a column poke.</b>
    /// <c>CompanyWatchCriterionMaterialiser</c> stamps <c>materialised_at</c> from
    /// <c>clock.UtcNow</c> and from nothing else, so a run driven by a clock reading four days ago
    /// writes precisely the row a run four days ago wrote. The criterion then simply ages, which is
    /// the state the read-side bound exists for: the job catches per-criterion exceptions and
    /// continues, so a criterion it cannot process keeps its old row while its siblings advance.
    /// </para>
    ///
    /// <para>
    /// Constructed directly rather than resolved, for the reason
    /// <c>CompanyWatchCriterionMaterialisationTests.RunDisabledAsync</c> gives: the clock is a
    /// dependency the fixture binds to the system one. Everything else comes from the real graph and
    /// the options are the shipped defaults, so only the instant differs.
    /// </para>
    /// </summary>
    private async Task RunMaterialiserAsAtAsync(DateTimeOffset at, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var materialiser = new CompanyWatchCriterionMaterialiser(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<CompanyWatchCriterionMemberStore>(),
            new FixedClock(at),
            Options.Create(new CompanyWatchMaterialisationOptions()),
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<CompanyWatchCriterionMaterialiser>.Instance);

        await materialiser.MaterialiseAsync(ct);
    }

    /// <summary>
    /// The state row as it physically stands, so the age pins can assert what the WRITER produced
    /// before claiming anything about what the reader does with it (CLAUDE.md §5 <c>Tests:</c>).
    /// Parity <c>CompanyWatchCriterionMaterialisationTests.ReadStateAsync</c>, including the absence
    /// of column aliases: the context applies its snake_case convention to unmapped SqlQuery types
    /// too, so an alias would have to fight that rather than help it.
    /// </summary>
    private async Task<StateRow?> ReadMaterialisationAsync(
        CompanyWatchCriterionId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Database
            .SqlQueryRaw<StateRow>(
                """
                SELECT state, materialised_at, criteria_fingerprint
                FROM company_watch_criterion_materialisations
                WHERE criterion_id = {0};
                """,
                id.Value)
            .ToListAsync(ct);

        return rows.Count == 0 ? null : rows[0];
    }

    private sealed record StateRow(
        string State, DateTimeOffset MaterialisedAt, string CriteriaFingerprint);

    /// <summary>
    /// The aggregate's own predicate transition — the production edit path, not a column poke. The
    /// staleness pin turns on this being the SAME call the update handler makes, because what it
    /// measures is what a read sees between that save and the next materialisation run.
    /// </summary>
    private async Task UpdateCriteriaAsync(
        CompanyWatchCriterionId id, CompanyWatchCriteriaSpec criteria, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, ct);
        criterion.UpdateCriteria(criteria, new FixedClock(T0.AddDays(1))).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The aggregate's own LABEL transition, and it returns the <c>UpdatedAt</c> stamps either side of
    /// it. The rename pin needs both: without evidence that the rename MOVED the row's mtime, it
    /// cannot rule out the timestamp guard it exists to rule out.
    /// </summary>
    private async Task<(DateTimeOffset Before, DateTimeOffset After)> RenameCriterionAsync(
        CompanyWatchCriterionId id, string label, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, ct);
        var before = criterion.UpdatedAt;

        criterion.Rename(label, new FixedClock(T0.AddDays(1))).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        return (before, criterion.UpdatedAt);
    }

    // House idiom: a private fixed clock per suite (parity CompanyWatchCriterionMaterialisationTests).
    // §5 forbids DateTime.UtcNow in production; the seed path takes the same injected
    // IDateTimeProvider production does, so the timestamps a test writes are produced the way
    // production produces them.
    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    /// <summary>
    /// #1681 part 2 — the pair of facts the ad-side plans now claim, and they only work TOGETHER.
    ///
    /// <para>
    /// The POSITIVE half names the member table's PK index, for exactly the reason the GIN assertions
    /// are positive rather than a "no Seq Scan" negative (dotnet-architect Q1(a)): a negative passes
    /// whenever some other path is available. The NEGATIVE half is what ADR 0139 actually claims — the
    /// register is not in this plan at all — and it is the one that cannot validate itself, which is
    /// why <c>CompanyQueries_StillReadTheRegister_SoTheAbsenceAssertionsCanFail</c> exists.
    /// </para>
    /// </summary>
    /// <summary>
    /// #1681 part 2 — the port now depends on the materialisation options (the read-side age bound)
    /// and a clock. Built HERE, once, so every test in this file exercises the SAME configuration
    /// production ships: the option's own default, never a value chosen to make a test pass. A test
    /// that wanted a different age would have to say so at its own call site, which is the point.
    /// </summary>
    private static CompanyWatchBrowseQuery PortFor(AppDbContext db) =>
        new(db,
            Options.Create(new CompanyWatchMaterialisationOptions()),
            new DateTimeProvider());

    private static void AssertReadsTheMemberSet(string plan, string which)
    {
        plan.ShouldContain(
            MemberPkIndexName,
            customMessage:
                $"The {which} query's plan does NOT use {MemberPkIndexName}. The member lookup is then "
                + "not the index lookup the breadth-gate bound was derived against (Index Only Scan, "
                + "Heap Fetches: 0), and the bound's derivation no longer describes what runs. The "
                + "usual causes are a missing ANALYZE on the materialisation tables (the job does one "
                + "per completed run) and a rewrite of the `= ANY(ARRAY(subselect))` shape into a "
                + $"JOIN — which the port's docblock forbids by name.{Environment.NewLine}"
                + $"Plan:{Environment.NewLine}{plan}");

        plan.ShouldNotContain(
            RegisterTable,
            customMessage:
                $"The {which} query's plan reads {RegisterTable} again. That is ADR 0139 undone: the "
                + "predicate's expensive half is back inside a request on the 300 ms MeListRead "
                + "budget, and — worse — the criterion would then be answered from TWO resolutions of "
                + "the same register predicate at two instants, which is the #1407/#1471 divergence "
                + $"the materialisation exists to close.{Environment.NewLine}"
                + $"Plan:{Environment.NewLine}{plan}");
    }

    /// <summary>
    /// EXPLAINs one of the MATERIALISED ad statements, keyed on the criterion the fixture's
    /// materialisation run wrote a member set for. Same instrument as <see cref="ExplainAsync"/> —
    /// including <c>enable_seqscan = off</c>, and for the same reason: the member set in a fixture is
    /// small enough that a sequential scan is genuinely the cheapest plan, so without the GUC the
    /// eligibility claim would be untestable. What the pin guarantees is that the shape the port emits
    /// is one the member PK CAN serve, which is precisely what a JOIN rewrite would not be.
    /// </summary>
    private static async Task<string> ExplainMaterialisedAsync(
        ScopedContext ctx,
        Func<NpgsqlConnection, CompanyWatchCriterionId, CriteriaFingerprint, NpgsqlCommand> build,
        CancellationToken ct)
    {
        var connection = (NpgsqlConnection)ctx.Db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var tx = await connection.BeginTransactionAsync(ct);

        await using (var guc = connection.CreateCommand())
        {
            guc.Transaction = tx;
            guc.CommandText = "SET LOCAL enable_seqscan = off;";
            await guc.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = build(connection, ctx.CriterionId, ctx.Fingerprint);
        cmd.Transaction = tx;
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
                // "552" + 7 digits = 10. The third digit is what
                // OrganizationNumber.IsPersonnummerShaped() reads, and '2' is the first LEGAL-ENTITY
                // value — see the AdOrgNr comment for why an "550…" seed would silently empty every
                // materialised member set in this file.
                OrganizationNumber = $"552{i:D7}",
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

        /// <summary>
        /// #1681 part 2 — the criterion the fixture's materialisation run wrote a member set for, and
        /// the digest of the predicate it was written FROM. Both are what the ad statements are keyed
        /// on, so carrying them here is what stops every ad test re-deriving the pair (and getting the
        /// fingerprint subtly wrong once).
        ///
        /// <para>
        /// Default (<c>Guid.Empty</c> / an empty digest) for a context built by
        /// <see cref="SeededContextAsync"/> alone, which seeds no criterion — the register-side pins
        /// never read either.
        /// </para>
        /// </summary>
        public CompanyWatchCriterionId CriterionId { get; set; }

        public CriteriaFingerprint Fingerprint { get; set; }

        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }
}
