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
/// <i>"the index is UNUSABLE"</i> from <i>"usable but not chosen"</i> — yet only the first is the bug. With
/// seqscan priced at ~1e10 the planner takes any usable index, so a surviving <c>Seq Scan on job_ads</c>
/// proves UNUSABILITY: exactly the predicate-implication failure, and independent of row count and ANALYZE
/// state. <c>SET LOCAL</c> runs in an always-rolled-back transaction, so it cannot ride a pooled connection
/// into another test.
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

    // The q-search shape ListJobAds actually emits: the status gate (JobAdSearchComposition.ApplyFilter:65
    // — the SPOT, ADR 0032-amendment 2026-05-23) AND a DISJUNCTION of the FTS branch and the title-trigram
    // substring fallback. Postgres serves it as a BitmapOr over BOTH GIN indexes, so this one query is the
    // oracle for both. Mirrors Jobbliggaren.Migrate --explain-search.
    private const string QSearchSql =
        "SELECT id FROM job_ads WHERE status = 'Active' " +
        "AND (search_vector @@ websearch_to_tsquery('swedish', 'lärare') " +
        "OR lower(title) LIKE '%lärare%')";

    // SuggestJobAdTermsQueryHandler:37-44 — status-gated, left-anchored LIKE with an explicit ESCAPE.
    private const string SuggestSql =
        @"SELECT title FROM job_ads WHERE status = 'Active' " +
        @"AND lower(title) LIKE 'lärar%' ESCAPE '\'";

    [Fact]
    public async Task QSearch_IsServedByBothTheFtsAndTitleTrigramIndexes()
    {
        var ct = TestContext.Current.CancellationToken;
        var plan = await ExplainWithoutSeqScanAsync(QSearchSql, ct);

        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            "/jobb q-search CANNOT use its indexes: PostgreSQL fell back to a sequential scan even with " +
            "seqscan priced at ~1e10, which means an index is UNUSABLE for this query rather than merely " +
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
        var plan = await ExplainWithoutSeqScanAsync(SuggestSql, ct);

        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            "title-suggest CANNOT use any index: PostgreSQL fell back to a sequential scan even with " +
            $"seqscan priced at ~1e10 — the index is unusable for this query. Plan:\n{plan}");

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

    private async Task<string> ExplainWithoutSeqScanAsync(string sql, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // SET LOCAL + a transaction we never commit: the override dies with the transaction and can never
        // ride a pooled connection into another test.
        await using var tx = await connection.BeginTransactionAsync(ct);

        await using (var set = connection.CreateCommand())
        {
            set.Transaction = tx;
            set.CommandText = "SET LOCAL enable_seqscan = off;";
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
