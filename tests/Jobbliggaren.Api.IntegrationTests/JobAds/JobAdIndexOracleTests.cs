using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #821 — the INDEX-EXISTENCE ORACLE for <c>job_ads</c>, against real Postgres.
/// <para>
/// <b>Why this file exists.</b> Five <c>job_ads</c> indexes embedded <c>deleted_at</c> in their
/// partial predicate, and EVERY ONE of them was created with raw <c>migrationBuilder.Sql</c> — the
/// Npgsql fluent API cannot express partial / functional / shadow-property indexes. Consequence:
/// <b>EF's <c>AppDbContextModelSnapshot</c> does not know these indexes exist.</b> Grep it: the only
/// <c>job_ads</c> index it carries is the fluent <c>ix_job_ads_external_source_external_id</c>.
/// </para>
/// <para>
/// PostgreSQL's <c>ALTER TABLE ... DROP COLUMN</c> states plainly: <i>"Indexes and table constraints
/// involving the column will be automatically dropped as well."</i> No error. No <c>CASCADE</c> needed.
/// So a scaffolded <c>DropColumn("deleted_at")</c> — which is EXACTLY what <c>dotnet ef migrations
/// add</c> emits, verified — would have silently destroyed all five, and EF could not have rebuilt
/// what it never knew about. The FTS search, both trigram indexes, the matching engine's lexeme GIN
/// and the title-suggest index would all be GONE, with a green migration and a green CI, and
/// <c>/jobb</c> would seq-scan.
/// </para>
/// <para>
/// Before this file, <b>the repo had zero tests asserting any of these indexes exist.</b> That
/// absence is precisely why the silent drop would have shipped. This is the guard.
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdIndexOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The five indexes that carried a `deleted_at` predicate before #821, each paired with the
    // access-method + expression fragment its definition must still contain. Names AND fragments copied
    // verbatim from the migration sources (NOT from prose — a wrong name makes `DROP INDEX IF EXISTS` a
    // silent no-op, and `DROP COLUMN` then removes the real index anyway: two silent failures composing).
    //
    // The fragment is load-bearing, not decoration: asserting only the NAME would let a rebuild silently
    // change the access method or the operator class (say, btree-on-title where a GIN trigram index used
    // to be) and still pass. The opclasses in particular are not interchangeable — `extracted_lexemes`
    // MUST keep default jsonb_ops, because jsonb_path_ops does not support the `?|` (exists-any) operator.
    //
    // Two of the five (`description_lower_trgm`, `extracted_lexemes`) currently have NO production reader
    // at all (#867) — they are still asserted here because #821's safety rests on ZERO delta: whatever the
    // schema had before, it must have after. Deciding their fate is #867's job, not this PR's.
    // The fragments are the NORMALISED form PostgreSQL itself reports in pg_indexes.indexdef — read
    // empirically out of the catalog, never reasoned out. Note the asymmetry, which is exactly why:
    // `title` is a varchar column, so Postgres rewrites `lower(title)` into `lower((title)::text)`;
    // `description` is `text`, so it needs no cast and stays `lower(description)`. Asserting the form we
    // WROTE instead of the form Postgres STORES is how this test failed on its first two runs.
    public static readonly TheoryData<string, string> RebuiltIndexes = new()
    {
        { "ix_job_ads_search_vector", "USING gin (search_vector)" },                                // /jobb FTS
        { "ix_job_ads_title_lower_trgm", "USING gin (lower((title)::text) gin_trgm_ops)" },         // q-search
        { "ix_job_ads_description_lower_trgm", "USING gin (lower(description) gin_trgm_ops)" },     // #867: no reader
        { "ix_job_ads_extracted_lexemes", "USING gin (extracted_lexemes)" },                        // #867: no reader
        { "ix_job_ads_title_lower_prefix", "USING btree (lower((title)::text) text_pattern_ops)" }, // suggest
    };

    [Theory]
    [MemberData(nameof(RebuiltIndexes))]
    public async Task JobAdIndex_SurvivesTheDeletedAtRetirement(string indexName, string expectedDefinition)
    {
        var ct = TestContext.Current.CancellationToken;
        var def = await ReadIndexDefAsync(indexName, ct);

        def.ShouldNotBeNull(
            $"{indexName} is MISSING from job_ads. DROP COLUMN silently drops every index whose " +
            "predicate names the dropped column, and EF's model snapshot does not know these five " +
            "raw-SQL indexes exist — so nothing else in the build would have told you. See #821.");

        def!.ShouldContain(expectedDefinition, Case.Insensitive,
            $"{indexName} exists but was rebuilt with the WRONG shape. Its access method / expression / " +
            $"operator class must be unchanged by #821 (only the WHERE clause was removed). Actual:\n{def}");
    }

    [Theory]
    [MemberData(nameof(RebuiltIndexes))]
    public async Task JobAdIndex_CarriesNoPredicate(string indexName, string expectedDefinition)
    {
        _ = expectedDefinition;
        var ct = TestContext.Current.CancellationToken;
        var def = await ReadIndexDefAsync(indexName, ct);
        def.ShouldNotBeNull();

        // senior-cto-advisor bind (#821 Q2 = (ii)): NO lifecycle-derived predicate on job_ads indexes,
        // ever again. A partial index is only usable when the query's WHERE *implies* the index's
        // WHERE — an implicit, uncheckable coupling between storage and a query detail. This repo has
        // already paid for it once, at ~35-50 s: F6P4aJobAdTrigramIndexPredicateFix exists because a
        // `status = 'Active'` index predicate outlived the query that implied it, and q-search ran on
        // a seq scan with the index built but unused. A predicate-free index cannot mismatch.
        //
        // The tempting `WHERE status = 'Active'` also buys nothing: every ad is BORN Active (the JobAd
        // constructor), so it excludes no row at ingest — it only evicts rows later, at archival,
        // adding GIN maintenance to the bulk archival jobs.
        def!.ShouldNotContain(" WHERE ",
            Case.Insensitive,
            $"{indexName} has regrown a partial predicate. If a read path stops emitting that exact " +
            "predicate, PostgreSQL cannot prove query-WHERE => index-WHERE and the index becomes " +
            "UNUSABLE — silently, with no error and no failing test. That is the 35-50 s bug of " +
            "2026-05-21, re-armed. See #821 Q2.");
    }

    private async Task<string?> ReadIndexDefAsync(string indexName, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'job_ads' AND indexname = @name;";
        cmd.Parameters.Add(NamedParameter(cmd, "@name", indexName));

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    private static DbParameter NamedParameter(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }
}
