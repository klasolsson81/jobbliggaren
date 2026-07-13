using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #821 — the PLANNER-USABILITY ORACLE. Asserting that an index EXISTS is not the same as asserting
/// that PostgreSQL can USE it, and the difference has already cost this product ~35-50 s of latency.
/// <para>
/// <b>The bug this guards against.</b> A partial index is usable only when the query's <c>WHERE</c>
/// <i>implies</i> the index's <c>WHERE</c>. On 2026-05-21 the trigram indexes carried
/// <c>WHERE status = 'Active' AND deleted_at IS NULL</c> while the q-search path emitted only
/// <c>deleted_at IS NULL</c> — so the planner could not prove implication, chose a seq scan, and
/// q-search sat at ~35-50 s <b>with the index built and green</b>. It was found by a human noticing
/// slowness, not by CI: the repo had (and until this file, still had) <b>zero</b> guards of this class.
/// #821 removes the failure mode by making every job_ads index predicate-free — and this file is what
/// keeps it removed.
/// </para>
/// <para>
/// <b>Why <c>SET LOCAL enable_seqscan = off</c> is the right instrument, and the obvious test is not.</b>
/// The naive version ("seed rows, EXPLAIN, hope the planner picks the index") is both flaky and blind:
/// on a small table the planner rationally prefers a seq scan even when the index is perfectly usable,
/// so the test would fail for the wrong reason — and, worse, it cannot distinguish <i>"the index is
/// UNUSABLE"</i> from <i>"the index is usable but not chosen"</i>. Those are the two cases we must tell
/// apart, because only the first is the bug. With <c>enable_seqscan = off</c> a seq scan is priced at
/// ~1e10, so the planner will take any usable index — meaning a remaining <c>Seq Scan on job_ads</c>
/// proves the index is <b>unusable</b>, which is exactly the predicate-implication failure. The result
/// is independent of row count and of <c>ANALYZE</c> state: not flaky, and it fails only for the real
/// reason.
/// </para>
/// <para>
/// <c>SET LOCAL</c> inside a transaction that is always rolled back — so the setting cannot leak onto a
/// pooled connection and silently distort another test's plan.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdPlannerUsabilityOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Each row: the read path, the SQL SHAPE production emits for it, and the index that must serve it.
    // The shapes carry `status = 'Active'` because JobAdSearchComposition.ApplyFilter (the SPOT, ADR
    // 0032-amendment 2026-05-23) puts it on every end-user read — and they carry NO `deleted_at`,
    // because #821 retired that axis. A predicate-free index is usable under both, which is the point.
    public static readonly TheoryData<string, string, string> ReadPaths = new()
    {
        {
            "/jobb FTS search",
            "SELECT id FROM job_ads WHERE status = 'Active' " +
            "AND search_vector @@ websearch_to_tsquery('swedish', 'lärare')",
            "ix_job_ads_search_vector"
        },
        {
            "/jobb q-search (title trigram)",
            "SELECT id FROM job_ads WHERE status = 'Active' AND lower(title) LIKE '%lärare%'",
            "ix_job_ads_title_lower_trgm"
        },
        {
            "/jobb q-search (description trigram)",
            "SELECT id FROM job_ads WHERE status = 'Active' AND lower(description) LIKE '%lärare%'",
            "ix_job_ads_description_lower_trgm"
        },
        {
            "matching engine (lexeme overlap)",
            "SELECT id FROM job_ads WHERE status = 'Active' " +
            "AND extracted_lexemes ?| array['larar']",
            "ix_job_ads_extracted_lexemes"
        },
        {
            "title suggest (left-anchored prefix)",
            "SELECT title FROM job_ads WHERE status = 'Active' AND lower(title) LIKE 'lärar%'",
            "ix_job_ads_title_lower_prefix"
        },
    };

    [Theory]
    [MemberData(nameof(ReadPaths))]
    public async Task ReadPath_CanUseItsIndex(string path, string sql, string expectedIndex)
    {
        var ct = TestContext.Current.CancellationToken;
        var plan = await ExplainWithoutSeqScanAsync(sql, ct);

        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            $"the {path} read path CANNOT use {expectedIndex}: PostgreSQL fell back to a sequential " +
            "scan even with seqscan priced at ~1e10, which means the index is UNUSABLE for this query, " +
            $"not merely un-preferred. This is the 2026-05-21 predicate-implication bug (~35-50 s). " +
            $"Plan:\n{plan}");

        plan.ShouldContain(expectedIndex, Case.Insensitive,
            $"the {path} read path did not use {expectedIndex}. Plan:\n{plan}");
    }

    private async Task<string> ExplainWithoutSeqScanAsync(string sql, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);

        // SET LOCAL + a transaction we never commit: the override dies with the transaction and can
        // never ride a pooled connection into another test.
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
                plan.AppendLine(reader.GetString(0));
        }

        await tx.RollbackAsync(ct);
        return plan.ToString();
    }
}
