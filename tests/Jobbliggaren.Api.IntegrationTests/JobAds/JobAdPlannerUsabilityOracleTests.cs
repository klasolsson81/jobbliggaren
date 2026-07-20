using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #821 — the PLANNER-USABILITY ORACLE. An index EXISTING is not the same as PostgreSQL being able to
/// USE it, and the gap between those two has already cost this product ~35-50 s of latency.
/// <para>
/// <b>The bug this guards.</b> A partial index is usable only when the query's <c>WHERE</c> <i>implies</i>
/// the index's <c>WHERE</c>. On 2026-05-21 the trigram indexes carried <c>WHERE status = 'Active' AND
/// deleted_at IS NULL</c> while q-search emitted only <c>deleted_at IS NULL</c>; the planner could not
/// prove implication, chose a seq scan, and q-search sat at ~35-50 s <b>with the index built and green</b>.
/// A human noticed the slowness — CI could not, because the repo had zero guards of this class. #821 kills
/// the failure mode by making every job_ads index predicate-free; this file keeps it killed.
/// </para>
/// <para>
/// <b><c>SET LOCAL enable_seqscan = off</c> is the instrument, and the obvious test is the wrong one.</b>
/// "Seed rows, EXPLAIN, hope the planner picks the index" is flaky AND blind: on a small table the planner
/// rationally prefers a seq scan even over a perfectly usable index, and the test cannot distinguish
/// <i>"the index is UNUSABLE"</i> from <i>"usable but not chosen"</i> — yet only the first is the bug. On
/// PostgreSQL 17+ (18.3 here) <c>enable_seqscan = off</c> does not add a cost: the planner counts
/// <c>disabled_nodes</c> and prefers ANY seq-scan-free path REGARDLESS of cost, so it is an effective
/// PROHIBITION as soon as a usable index exists. A surviving <c>Seq Scan on job_ads</c> therefore proves
/// UNUSABILITY: exactly the predicate-implication failure, and independent of row count and ANALYZE state.
/// (This mirrors the truth-sync in <see cref="Jobbliggaren.Worker.IntegrationTests.CompanyWatches"/>'s
/// browse-query plan test — the enable_* GUCs are prohibitions on 17+, not the pre-17 cost penalty.)
/// <c>SET LOCAL</c> runs in an always-rolled-back transaction, so it cannot ride a pooled connection into
/// another test.
/// </para>
/// <para>
/// <b>The same instrument, applied to the ORDER half: <c>SET LOCAL enable_sort = off</c>.</b> A filter+order
/// query has two eligibility questions — CAN the index serve the <c>WHERE</c>, and CAN it serve the
/// <c>ORDER BY</c> — and each needs its own proof. seqscan-off answers the first; sort-off answers the
/// second. Without it the browse-sort fact was a KNOWN intermittent CI flake (the fact was added by #743 /
/// PR #970; it was later seen failing on PR #1010 and rerun-recovered — see the memory
/// <c>reference_browse_sort_plan_test_flakes_on_small_shared_db</c>): with only the FILTER half pinned, the
/// ORDER half stayed a cost-based choice, and when the shared <c>[Collection("Api")]</c> Postgres estimated a
/// TINY active-row count (reproduced 2026-07-20 at 200 Archived + a handful of Active + ANALYZE, planner
/// <c>rows=3</c>) it chose a <c>Bitmap Index Scan + Sort</c> over the ordered index walk — near cost-equal
/// there, so the choice flipped with the shared DB's execution-order-dependent statistics. Disabling sort
/// forces the ordered walk whenever the index CAN serve the order, so a surviving <c>Sort Key:</c> proves the
/// order is index-UNSERVABLE — the exact mirror of a surviving <c>Seq Scan</c>, equally
/// statistics-independent. Because the GUC is a prohibition, not a cost tweak, a sort that is the ONLY route
/// to correct ordering is still emitted (every candidate plan then carries the disabled Sort node), so the
/// instrument can never mask a real ordering regression. This stays an ELIGIBILITY claim (CAN the index serve
/// the order), never a plan-CHOICE claim: the eligibility-vs-choice line is the #875 charter, and production's
/// plan choice at corpus cardinality is a separate, Klas-gated guard tracked as its own follow-up (#1013).
/// </para>
/// <para>
/// <b>Every shape below is one PRODUCTION ACTUALLY EMITS — and the first draft of this file got that wrong.</b>
/// It asserted three shapes the product never runs: a bare <c>lower(description) LIKE</c> (ADR 0062
/// deliberately KILLED that query — <c>JobAdSearchComposition.cs:50</c>, "description-LIKE körs ALDRIG"), an
/// <c>extracted_lexemes ?|</c> in a WHERE (the only <c>?|</c> in <c>src/</c> sits inside an ORDER BY CASE,
/// which no GIN index can serve), and the FTS/trigram branches as isolated conjuncts when production emits
/// them as a DISJUNCTION. Those rows were green and proved nothing — the same lying-instrument defect this
/// PR fixes in <c>--explain-search</c>, reproduced inside the guard written to prevent it. Caught by
/// dotnet-architect and code-reviewer independently.
/// </para>
/// <para>
/// The consequence is a finding in its own right: <c>ix_job_ads_description_lower_trgm</c> and
/// <c>ix_job_ads_extracted_lexemes</c> have <b>no production reader at all</b> → <b>#867</b>. The migration
/// still re-creates them (zero-delta is what makes #821 safe to merge and to revert) and their existence is
/// still pinned by <see cref="JobAdIndexOracleTests"/> — but they get no planner assertion here, because
/// there is no query to assert. An oracle for a query nobody runs is decoration.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdPlannerUsabilityOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The q-search DISJUNCTION ListJobAds emits: the FTS branch OR the title-trigram substring fallback,
    // served as a BitmapOr over BOTH GIN indexes — this one query is the oracle for both. Mirrors the
    // disjunction in JobAdSearchComposition.ApplyFilter / Jobbliggaren.Migrate --explain-search.
    //
    // Deliberately WITHOUT the `status='Active'` conjunct that production ALSO emits (ApplyFilter SPOT).
    // #743 added ix_job_ads_status_published_at_id, whose leading `status` key gives that conjunct its own
    // Index Cond; on the empty Testcontainers table the planner then serves the whole query via that btree
    // and never reaches the GINs — collapsing this oracle's power to prove GIN predicate-implication. The
    // status conjunct is ORTHOGONAL to whether the FTS/title-trigram GINs are usable for their arms; its
    // index-servability is proven separately by
    // DefaultBrowseSort_IsIndexServedForBothFilterAndOrder_NoSeqScanNoSort. This
    // is the file's "shape production emits" rule APPLIED, not broken: the disjunction is verbatim
    // production (same operators, same form) and isolating it proves exactly the #821 property this file
    // owns — keeping the orthogonal conjunct would only let the oracle go green via a different index.
    private const string QSearchSql =
        "SELECT id FROM job_ads WHERE (search_vector @@ websearch_to_tsquery('swedish', 'lärare') " +
        "OR lower(title) LIKE '%lärare%')";

    // SuggestJobAdTermsQueryHandler:37-44 — left-anchored LIKE with an explicit ESCAPE. WITHOUT the
    // production `status='Active'` conjunct, same reason as QSearchSql: #743's status index would otherwise
    // serve that conjunct on empty data and mask whether the title prefix/trigram index is usable.
    private const string SuggestSql =
        @"SELECT title FROM job_ads WHERE lower(title) LIKE 'lärar%' ESCAPE '\'";

    // #743 — the default no-facet /jobb browse ITEMS query production emits: status = 'Active'
    // (JobAdSearchComposition.ApplyFilter SPOT) + PublishedAtDesc sort (ApplySort:263 —
    // `ORDER BY published_at DESC, id`) + the page window (LIMIT = ListJobAdsQuery.PageSize default 20).
    // The projection columns don't change the scan/sort nodes, so this pins the filter+order shape that
    // determines index-servability.
    private const string BrowseSortSql =
        "SELECT id FROM job_ads WHERE status = 'Active' " +
        "ORDER BY published_at DESC, id LIMIT 20";

    [Fact]
    public async Task QSearch_IsServedByBothTheFtsAndTitleTrigramIndexes()
    {
        var ct = TestContext.Current.CancellationToken;
        var plan = await ExplainWithScanAndSortDisabledAsync(QSearchSql, ct);

        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            "/jobb q-search CANNOT use its indexes: PostgreSQL fell back to a sequential scan even with " +
            "seqscan disabled (a prohibition on PG 17+), which means an index is UNUSABLE for this query rather than merely " +
            $"un-preferred. That is the 2026-05-21 predicate-implication bug (~35-50 s). Plan:\n{plan}");

        // The disjunction must be served by BOTH arms (a BitmapOr). If only one index appears, the other
        // arm is scanning inside the Or — the same bug wearing a different hat.
        plan.ShouldContain("ix_job_ads_search_vector", Case.Insensitive,
            $"the FTS arm of q-search did not use its GIN index. Plan:\n{plan}");
        plan.ShouldContain("ix_job_ads_title_lower_trgm", Case.Insensitive,
            $"the title-trigram arm of q-search did not use its GIN index. Plan:\n{plan}");
    }

    [Fact]
    public async Task TitleSuggest_IsIndexServed()
    {
        var ct = TestContext.Current.CancellationToken;
        var plan = await ExplainWithScanAndSortDisabledAsync(SuggestSql, ct);

        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            "title-suggest CANNOT use any index: PostgreSQL fell back to a sequential scan even with " +
            $"seqscan disabled (a prohibition on PG 17+) — the index is unusable for this query. Plan:\n{plan}");

        // Deliberately NOT pinned to one index name. Dropping the predicates (#821 Q2 = (ii)) means
        // `lower(title) LIKE 'lärar%'` can now be served by EITHER the btree text_pattern_ops prefix index
        // OR the GIN trigram index over the same expression — and the planner's choice between two USABLE
        // indexes is cost-based, i.e. driven by reltuples, i.e. by whatever other tests in the shared
        // [Collection("Api")] Postgres happened to seed first. Asserting a name would be an order-dependent
        // flake dressed up as a guarantee. What must hold is that the path is INDEX-SERVED at all; both
        // candidates are separately pinned to exist, predicate-free, by JobAdIndexOracleTests.
        var indexServed =
            plan.Contains("ix_job_ads_title_lower_prefix", StringComparison.OrdinalIgnoreCase) ||
            plan.Contains("ix_job_ads_title_lower_trgm", StringComparison.OrdinalIgnoreCase);

        indexServed.ShouldBeTrue(
            "title-suggest was served by neither the btree prefix index nor the title trigram index. " +
            $"Plan:\n{plan}");
    }

    [Fact]
    public async Task DefaultBrowseSort_IsIndexServedForBothFilterAndOrder_NoSeqScanNoSort()
    {
        // #743 — the browse-sort index (status, published_at DESC, id) must serve BOTH halves of the most-
        // hit anonymous query: the status='Active' filter AND the published_at DESC, id ORDER BY. Before it,
        // job_ads had zero index on those columns → Seq Scan + top-N heapsort of ~42k active rows per page.
        var ct = TestContext.Current.CancellationToken;
        var plan = await ExplainWithScanAndSortDisabledAsync(BrowseSortSql, ct);

        // Filter half: with seqscan disabled (a prohibition on PG 17+) a surviving Seq Scan proves the index
        // is UNUSABLE for this query (the #821 predicate-implication failure mode), not merely un-preferred.
        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            $"the default browse CANNOT use an index for its status filter — Seq Scan survived. Plan:\n{plan}");

        // Order half: with sort ALSO disabled (see the helper), the index's column order
        // (status asc, published_at DESC, id asc) must yield the page window already sorted. A surviving
        // Sort / Incremental Sort node means the index cannot serve the order — the ~42k-row heapsort is
        // still paid and the whole point of #743 is unmet.
        //
        // Anchor on "Sort Key:", not the bare substring "Sort". Every actual sort node in an EXPLAIN plan —
        // Sort, Incremental Sort, and sorts under Gather Merge / Unique / WindowAgg — emits a "Sort Key:"
        // detail line; an ordered Index (Only) Scan emits none. The bare "Sort" substring is a latent
        // false-alarm surface — a future index or column name containing the letters "sort" would redden this
        // gate while the guarantee holds. Anchoring on "Sort Key:" mirrors the sibling
        // CompanyWatchBrowseQueryPlanTests.
        //
        // Written verdict — why `enable_sort = off` is load-bearing here, not decoration.
        // This fact was a KNOWN intermittent CI flake (reference_browse_sort_plan_test_flakes_on_small_shared_db;
        // the fact was added by #743 / PR #970, seen failing on PR #1010 and rerun-recovered). It ran under
        // `enable_seqscan = off` ALONE, which pins the FILTER half but leaves the ORDER half a cost-based
        // CHOICE. Reproduced 2026-07-20 (dedicated postgres:18 Testcontainer, 200 Archived + a handful of
        // Active + ANALYZE — the shared-DB character): at a tiny active estimate the planner picks bitmap+sort
        // over the ordered walk, because they are near cost-equal there —
        //     Limit → Sort (Sort Key: published_at DESC, id)
        //               → Bitmap Heap Scan → Bitmap Index Scan on ix_job_ads_status_published_at_id (rows=3)
        // — a surviving Sort Key ⇒ the assertion failed, execution-order-dependently. With `enable_sort = off`
        // added, the SAME state plans deterministically —
        //     Limit → Index Only Scan using ix_job_ads_status_published_at_id (rows=3)
        // — no Sort, at every measured active count {3,5,10}. So this is the ORDER-half eligibility mirror of
        // the FILTER-half seqscan GUC, statistics-independent, no seeding. It stays an ELIGIBILITY claim
        // (CAN the index serve the order), NOT production's plan CHOICE at corpus cardinality — the
        // eligibility-vs-choice line is the #875 charter, and that separate CHOICE guard is Klas-gated and
        // tracked as its own follow-up (#1013). enable_sort=off is inert for the two ORDER-BY-free facts
        // above (they emit no Sort).
        plan.ShouldNotContain("Sort Key:", Case.Insensitive,
            $"the browse-sort index does not serve the ORDER BY — a Sort survived even with sort disabled " +
            $"(its Sort Key line is present), so the order is index-unservable. Plan:\n{plan}");

        plan.ShouldContain("ix_job_ads_status_published_at_id", Case.Insensitive,
            $"the default browse was not served by the #743 browse-sort index. Plan:\n{plan}");
    }

    private async Task<string> ExplainWithScanAndSortDisabledAsync(string sql, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // SET LOCAL + a transaction we never commit: the overrides die with the transaction and can never
        // ride a pooled connection into another test.
        //
        // Two eligibility GUCs, one per half of a filter+order query. On PG 17+ (18.3 here) these are not
        // cost penalties but `disabled_nodes` prohibitions: the planner prefers any path that avoids the
        // disabled node whenever one exists, regardless of cost. enable_seqscan=off ⇒ a surviving `Seq Scan`
        // proves the FILTER index unusable; enable_sort=off ⇒ a surviving `Sort Key:` proves the ORDER is not
        // index-servable. Neither can mask a real regression: a scan/sort that is the ONLY correct plan is
        // still emitted (every candidate then carries the disabled node), so the test still reddens — they
        // only strip the statistics-driven CHOICE between a usable index and an equally-costed alternative,
        // which is what made the browse-sort fact flake. enable_sort=off is inert for the two ORDER-BY-free
        // shapes (QSearch/Suggest emit no Sort); if either ever grows an ORDER BY, re-confirm that before
        // relying on this shared helper.
        await using var tx = await connection.BeginTransactionAsync(ct);

        await using (var set = connection.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "SET LOCAL enable_seqscan = off; SET LOCAL enable_sort = off;";
            await set.ExecuteNonQueryAsync(ct);
        }

        var plan = new StringBuilder();
        await using (var explain = connection.CreateCommand())
        {
            explain.Transaction = tx;
            explain.CommandText = "EXPLAIN " + sql;

            await using var reader = await explain.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                plan.AppendLine(reader.GetString(0));
            }
        }

        await tx.RollbackAsync(ct);
        return plan.ToString();
    }
}
