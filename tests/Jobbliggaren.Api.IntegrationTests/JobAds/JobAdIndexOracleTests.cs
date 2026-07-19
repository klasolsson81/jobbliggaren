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

    /// <summary>
    /// #841 — the SEVEN facet indexes. #821 deliberately left these alone (its Q4 named them "#841's
    /// territory"), and #841 must prove it did not destroy them.
    ///
    /// <para>
    /// The danger here is the same one, one table over, and sharper. The scaffolded migration for #841
    /// emits <c>ALTER TABLE job_ads DROP COLUMN x; ALTER TABLE job_ads ADD x text;</c> for each of these
    /// seven columns — and <c>DROP COLUMN</c> takes every dependent index with it, silently. EF's model
    /// snapshot does not know these seven indexes exist (all raw <c>migrationBuilder.Sql</c>), so nothing
    /// in the build would have said a word. The hand-written migration uses
    /// <c>ALTER COLUMN … DROP EXPRESSION</c> precisely because it touches neither the values nor the
    /// indexes; this is the test that holds it to that.
    /// </para>
    ///
    /// <para>
    /// <b>These seven KEEP their <c>WHERE … IS NOT NULL</c> predicate, and that is not a contradiction of
    /// #821 Q2.</b> That bind bans <i>lifecycle-derived</i> predicates (<c>status = 'Active'</c>) — an
    /// implicit coupling between storage and a query detail that this repo has already paid ~35-50 s for.
    /// An <c>IS NOT NULL</c> predicate is NULL-SPARSITY: it is provably implied by the <c>IN (...)</c>
    /// filters that use these indexes (a NULL column can never be in a list of concept ids), it cannot
    /// drift as read paths change, and it keeps the index off the many ads that carry no facet at all.
    /// Which is why they are asserted in their own theory, and NOT by
    /// <see cref="JobAdIndex_CarriesNoPredicate"/>.
    /// </para>
    /// </summary>
    public static readonly TheoryData<string, string> FacetIndexes = new()
    {
        { "ix_job_ads_ssyk_concept_id", "USING btree (ssyk_concept_id)" },
        { "ix_job_ads_region_concept_id", "USING btree (region_concept_id)" },
        { "ix_job_ads_occupation_group_concept_id", "USING btree (occupation_group_concept_id)" },
        { "ix_job_ads_municipality_concept_id", "USING btree (municipality_concept_id)" },
        { "ix_job_ads_employment_type_concept_id", "USING btree (employment_type_concept_id)" },
        { "ix_job_ads_worktime_extent_concept_id", "USING btree (worktime_extent_concept_id)" },
        { "ix_job_ads_organization_number", "USING btree (organization_number)" },
    };

    [Theory]
    [MemberData(nameof(FacetIndexes))]
    public async Task FacetIndex_SurvivesTheMaterialisation(string indexName, string expectedDefinition)
    {
        var ct = TestContext.Current.CancellationToken;
        var def = await ReadIndexDefAsync(indexName, ct);

        def.ShouldNotBeNull(
            $"{indexName} is MISSING from job_ads. #841 converted its column from a STORED generated " +
            "column to an ordinary one. If that was done with DROP COLUMN + ADD COLUMN (which is what " +
            "`dotnet ef migrations add` emits — verified) rather than ALTER COLUMN ... DROP EXPRESSION, " +
            "Postgres dropped this index silently and EF could not rebuild what its snapshot never knew " +
            "about. Facet-filtered search and the matching engine would seq-scan job_ads. See #841.");

        def!.ShouldContain(expectedDefinition, Case.Insensitive,
            $"{indexName} exists but has the WRONG shape. #841 must not change the access method or the " +
            $"indexed expression — only the column's generated-ness. Actual:\n{def}");
    }

    [Theory]
    [MemberData(nameof(FacetIndexes))]
    public async Task FacetIndex_KeepsItsNullSparsityPredicate(string indexName, string expectedDefinition)
    {
        _ = expectedDefinition;
        var ct = TestContext.Current.CancellationToken;
        var def = await ReadIndexDefAsync(indexName, ct);
        def.ShouldNotBeNull();

        // The counterpart to JobAdIndex_CarriesNoPredicate, and the reason the two sets are separate.
        // #821 Q2 banned LIFECYCLE-derived predicates on job_ads indexes. It explicitly preserved these
        // seven: "those are null-sparsity predicates, provably implied by the IN (...) filters that use
        // them. They are #841's territory. Do not touch them."
        //
        // Losing the predicate here would not be a correctness bug, but it would silently bloat seven
        // indexes with every facet-less ad (every manually created ad, and every ad whose source omitted
        // that key) — so it is pinned, and the asymmetry with the other five is stated out loud rather
        // than left for someone to "harmonise" later.
        def!.ShouldContain("WHERE ", Case.Insensitive,
            $"{indexName} has LOST its `WHERE ... IS NOT NULL` predicate. That predicate is null-sparsity " +
            "(not lifecycle-derived), it is provably implied by every IN (...) query that uses this " +
            "index, and #821 Q2 explicitly preserved it. Do not harmonise these seven with the five " +
            "predicate-free indexes above — the two carry different kinds of predicate.");
    }

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

    [Fact]
    public async Task BrowseSortIndex_ExistsPredicateFreeBtreeOverStatusPublishedAtDescId()
    {
        // #743 — the default-browse-sort index. Fluent HasIndex (not raw SQL) so the model snapshot tracks
        // it and a future scaffold can never silently drop it (unlike the RebuiltIndexes above). Asserts the
        // shape empirically from pg_indexes.indexdef, not the form we wrote.
        var ct = TestContext.Current.CancellationToken;
        var def = await ReadIndexDefAsync("ix_job_ads_status_published_at_id", ct);

        def.ShouldNotBeNull(
            "ix_job_ads_status_published_at_id is MISSING — the default /jobb browse (status='Active' + " +
            "PublishedAt DESC, Id) would seq-scan + top-N-heapsort every active row per page view. See #743.");

        // btree over the three columns in ORDER-BY order, published_at DESC so the range scan is pre-sorted.
        def!.ShouldContain("USING btree", Case.Insensitive, $"wrong access method. Actual:\n{def}");
        def.ShouldContain("status", Case.Insensitive, $"missing status key column. Actual:\n{def}");
        def.ShouldContain("published_at DESC", Case.Insensitive,
            $"published_at must be DESC (mirrors the ORDER BY) or the scan is not pre-sorted. Actual:\n{def}");
        def.ShouldContain("id", Case.Insensitive, $"missing id tiebreak column. Actual:\n{def}");

        // #821 Q2 — predicate-free. `status` is a KEY column here, never a partial WHERE predicate: a
        // lifecycle-derived predicate can drift from the query's WHERE and go silently UNUSABLE (the
        // 35-50 s bug of 2026-05-21). A key column cannot drift.
        def.ShouldNotContain(" WHERE ", Case.Insensitive,
            $"ix_job_ads_status_published_at_id grew a partial predicate — #821 Q2 bans lifecycle-derived " +
            $"index predicates on job_ads. Use status as a key column, not a WHERE. Actual:\n{def}");
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
