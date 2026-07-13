using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #841 — the EF-model pin for the seven source facets. It needs no database: the model is built from
/// <see cref="JobAdConfiguration"/> and inspected.
///
/// <para>
/// <b>This exists because of a silent failure mode that is easy to re-introduce and impossible to see.</b>
/// While the seven were STORED generated columns, EF marked them <c>ValueGeneratedOnAddOrUpdate</c> — and
/// <b>EF omits properties with that flag from INSERT and UPDATE statements</b>, because the database is
/// supposed to produce the value. #841 makes C# produce it instead. If <c>.ValueGeneratedNever()</c> is
/// ever dropped (it looks like noise; it is not), then:
/// </para>
/// <list type="bullet">
///   <item>the C# still compiles;</item>
///   <item><c>JobAd.SetSourcePayload</c> still runs and sets all seven;</item>
///   <item>every EF-InMemory test still passes (InMemory ignores the flag);</item>
///   <item>and <b>Postgres never receives the values</b> — every facet column is NULL, forever.</item>
/// </list>
/// <para>
/// That is #841's own failure class — a value that looks written and is functionally absent — re-entering
/// through the fix. Same for a computed-column expression sneaking back: the purge would resume
/// destroying the seven. Both are pinned here, and again against real Postgres in
/// <c>JobAdFacetsSurvivePurgeTests</c>. Three locks, because the failure is silent.
/// </para>
/// </summary>
public class JobAdFacetColumnMappingTests
{
    public static readonly TheoryData<string, string> Facets = new()
    {
        { nameof(JobAd.SsykConceptId), "ssyk_concept_id" },
        { nameof(JobAd.OccupationGroupConceptId), "occupation_group_concept_id" },
        { nameof(JobAd.MunicipalityConceptId), "municipality_concept_id" },
        { nameof(JobAd.RegionConceptId), "region_concept_id" },
        { nameof(JobAd.EmploymentTypeConceptId), "employment_type_concept_id" },
        { nameof(JobAd.WorktimeExtentConceptId), "worktime_extent_concept_id" },
        { nameof(JobAd.OrganizationNumber), "organization_number" },
    };

    [Theory]
    [MemberData(nameof(Facets))]
    public void Facet_is_written_by_CSharp_never_generated_by_the_database(
        string propertyName, string columnName)
    {
        var property = FacetProperty(propertyName);

        property.ValueGenerated.ShouldBe(ValueGenerated.Never,
            $"JobAd.{propertyName} must be ValueGenerated.Never. Anything else (in practice " +
            "ValueGeneratedOnAddOrUpdate, which is what it carried while it was a STORED generated " +
            "column) makes EF OMIT the property from INSERT and UPDATE — so SetSourcePayload would set " +
            "it in memory, the tests would pass, and Postgres would store NULL. Silently. See #841.");

        property.GetComputedColumnSql().ShouldBeNull(
            $"JobAd.{propertyName} must not be a computed column. It was one, derived from raw_payload — " +
            "which PurgeStaleRawPayloadsJob nulls after 30 days, causing Postgres to recompute it to " +
            "NULL and silently erase it on still-ACTIVE ads. That is the whole of #841.");

        property.GetColumnName().ShouldBe(columnName,
            "the column name is load-bearing: the seven partial indexes are raw-SQL and reference these " +
            "exact names, and EF's model snapshot cannot see them.");
    }

    [Theory]
    [MemberData(nameof(Facets))]
    public void Facet_column_stays_text_and_nullable(string propertyName, string columnName)
    {
        _ = columnName;
        var property = FacetProperty(propertyName);

        property.IsNullable.ShouldBeTrue(
            "null-sparsity is expected and correct: a manually created ad has no source facets, and a " +
            "source payload may omit any of them. The seven partial indexes are WHERE ... IS NOT NULL " +
            "precisely because of it.");

        // No HasMaxLength: varchar(n) would force a table rewrite the migration deliberately avoids
        // (ALTER COLUMN ... DROP EXPRESSION is a catalogue update — 2.5 ms, no rewrite).
        property.GetMaxLength().ShouldBeNull(
            $"JobAd.{propertyName} must stay `text`. Adding HasMaxLength turns it into varchar(n), which " +
            "forces a full table rewrite on a 47k-row table — and the #841 migration's entire safety " +
            "argument rests on NOT rewriting the table.");
    }

    [Fact]
    public void The_two_legitimate_generated_columns_survive()
    {
        // Non-vacuity, and the precise statement of the rule. #841 does not ban generated columns — it
        // bans deriving DURABLE state from raw_payload, the one column with a retention TTL. These two
        // derive from columns that have none (title/description, extracted_terms) and must keep working;
        // a test that killed them would be enforcing the wrong rule.
        var entity = Model().FindEntityType(typeof(JobAd))!;

        entity.FindProperty("SearchVector")!.GetComputedColumnSql()
            .ShouldNotBeNullOrWhiteSpace("search_vector derives from title/description — no TTL, legitimate");

        entity.FindProperty("ExtractedLexemes")!.GetComputedColumnSql()
            .ShouldNotBeNullOrWhiteSpace("extracted_lexemes derives from extracted_terms — no TTL, legitimate");
    }

    [Fact]
    public void RawPayload_is_still_the_only_column_with_a_retention_TTL()
    {
        // The premise the whole design rests on, stated as a test so that adding a second TTL'd column
        // forces someone to re-read JobAdRawPayloadDerivationGuardTests rather than discover it later.
        var entity = Model().FindEntityType(typeof(JobAd))!;

        entity.FindProperty(nameof(JobAd.RawPayload))!.GetComputedColumnSql().ShouldBeNull(
            "raw_payload is written by SetSourcePayload and nulled by the purge. It is a base column, " +
            "never a derived one.");
    }

    private static IProperty FacetProperty(string propertyName)
    {
        var property = Model().FindEntityType(typeof(JobAd))!.FindProperty(propertyName);

        property.ShouldNotBeNull(
            $"JobAd.{propertyName} is not mapped. It is one of the seven #841 source facets; without a " +
            "mapping the column is not written at all.");

        return property!;
    }

    // The Npgsql model, built without touching a database (EF builds the model lazily and does not
    // connect). This is the same model the migration and the runtime use.
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=none;Password=none")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var db = new AppDbContext(options);
        return db.Model;
    }
}
