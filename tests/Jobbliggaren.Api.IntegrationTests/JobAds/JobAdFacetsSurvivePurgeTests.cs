using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #841 — THE regression test. It states the defect in the product's own terms and it fails on the code
/// that shipped before this PR.
///
/// <para>
/// <b>The defect.</b> The seven facet columns were Postgres STORED generated columns reading
/// <c>raw_payload</c>. <see cref="PurgeStaleRawPayloadsJob"/> nulls <c>raw_payload</c> 30 days after
/// publication (ADR 0032 §8), and Postgres RECOMPUTES a stored generated column whenever its base column
/// changes — so the purge silently nulled all seven, for an ad that was still ACTIVE and still listed.
/// Facet-filtered search, the per-user matching engine and the company-watch location filter all read
/// those columns, so the ad vanished from every one of them until the 02:00 sync rewrote the payload and
/// resurrected the columns: ~21.5 h of every 24, every day.
/// </para>
///
/// <para>
/// <b>Why this must run against real Postgres and can NEVER be an InMemory test.</b> InMemory ignores
/// <c>HasComputedColumnSql</c> entirely, so under InMemory the columns were always NULL and the bug was
/// invisible — which is exactly how it survived four separate column additions. It stays a Postgres test
/// after the fix for a second, sharper reason: while the columns were computed, EF marked them
/// <c>ValueGeneratedOnAddOrUpdate</c>, and <b>EF omits such properties from INSERT/UPDATE</b>. Drop
/// <c>.ValueGeneratedNever()</c> from <c>JobAdConfiguration</c> and the C# write still compiles,
/// <c>SetSourcePayload</c> still runs, every InMemory test still passes — and Postgres never receives the
/// value. Only a real database can see that.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class JobAdFacetsSurvivePurgeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private sealed record FacetRow(
        string? SsykConceptId,
        string? RegionConceptId,
        string? OccupationGroupConceptId,
        string? MunicipalityConceptId,
        string? EmploymentTypeConceptId,
        string? WorktimeExtentConceptId,
        string? OrganizationNumber,
        bool RawPayloadIsNull);

    [Fact]
    public async Task Purge_NullsTheRawPayload_ButLeavesAllSevenFacetsIntact()
    {
        var ct = TestContext.Current.CancellationToken;

        // An ad PAST the 30-day horizon — so the purge takes it — but still ACTIVE and still listed.
        // That combination is the whole point: this is not an expired ad, it is a live one the product
        // is supposed to keep showing.
        var title = await SeedImportedAdAsync(publishedDaysAgo: 40, ct);

        var before = await ReadFacetsAsync(title, ct);
        before.RawPayloadIsNull.ShouldBeFalse();
        before.SsykConceptId.ShouldBe("Ssyk_uwa_841");
        before.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6");
        before.OrganizationNumber.ShouldBe("5592804784");

        await RunPurgeAsync(ct);

        var after = await ReadFacetsAsync(title, ct);

        // The purge did its actual job: the payload is gone (GDPR Art. 5(1)(c)/(e)).
        after.RawPayloadIsNull.ShouldBeTrue(
            "PurgeStaleRawPayloadsJob must still null raw_payload past the retention horizon — the fix " +
            "does not weaken the data-minimisation control, it stops that control from destroying the " +
            "sanitized fields ADR 0032 §8 says are kept indefinitely.");

        // ...and every facet survived it. Before #841, all seven of these were NULL here.
        after.SsykConceptId.ShouldBe("Ssyk_uwa_841");
        after.RegionConceptId.ShouldBe("CaRE_1nn_hRb");
        after.OccupationGroupConceptId.ShouldBe("DJh5_yyF_hEM");
        after.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6");
        after.EmploymentTypeConceptId.ShouldBe("PFZr_Syz_cUq");
        after.WorktimeExtentConceptId.ShouldBe("6YE1_gAC_R2G");
        after.OrganizationNumber.ShouldBe("5592804784");
    }

    [Fact]
    public async Task Purge_LeavesThePurgedAdStillFindable_ByItsMunicipalityFacet()
    {
        // The same defect, stated as the user-visible symptom rather than as a column: an ad the purge
        // has touched must still be reachable through the facet-filtered read path. This is the assertion
        // that would have caught #841 from the outside.
        var ct = TestContext.Current.CancellationToken;
        var title = await SeedImportedAdAsync(publishedDaysAgo: 40, ct);

        await RunPurgeAsync(ct);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var municipalities = new[] { "AvNB_uwa_6n6" };
        var found = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Title == title)
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(j => municipalities.Contains(EF.Property<string?>(j, "MunicipalityConceptId")))
            .CountAsync(ct);

        found.ShouldBe(1,
            "a still-ACTIVE ad past the 30-day payload horizon must remain findable by its municipality " +
            "facet. Before #841 the purge nulled municipality_concept_id and this ad disappeared from " +
            "filtered search and from the matching engine for ~21.5h of every day.");
    }

    [Fact]
    public async Task Ingest_WritesAllSevenFacets_ToPostgres()
    {
        // The .ValueGeneratedNever() pin. If that flag is dropped, EF silently omits the seven from the
        // INSERT (they were ValueGeneratedOnAddOrUpdate while computed) and every one of these reads NULL
        // — while the C# still compiles and every InMemory test stays green.
        var ct = TestContext.Current.CancellationToken;
        var title = await SeedImportedAdAsync(publishedDaysAgo: 1, ct);

        var row = await ReadFacetsAsync(title, ct);

        row.SsykConceptId.ShouldNotBeNull(
            "the seven facets are written by C# now. A NULL here means EF omitted the column from the " +
            "INSERT — check that JobAdConfiguration still says .ValueGeneratedNever().");
        row.RegionConceptId.ShouldNotBeNull();
        row.OccupationGroupConceptId.ShouldNotBeNull();
        row.MunicipalityConceptId.ShouldNotBeNull();
        row.EmploymentTypeConceptId.ShouldNotBeNull();
        row.WorktimeExtentConceptId.ShouldNotBeNull();
        row.OrganizationNumber.ShouldNotBeNull();
    }

    [Fact]
    public async Task Ingest_WritesNull_NotEmptyString_WhenTheSourceOmitsAFacet()
    {
        // The empty-string invariant, proven where it matters: in the database.
        //
        // Postgres' ->>'concept_id' produced SQL NULL for a missing key. A C# parser naturally produces
        // "" — and "" IS NOT NULL, so it would enter the partial `WHERE ... IS NOT NULL` index as a row
        // that no IN (...) list can ever match: present, indexed, and invisible. That is #841's own
        // failure mode (a value that looks written and is functionally absent) re-entering through its
        // own fix. JobAdFacets normalises blank to null in its constructor; this is the proof.
        var ct = TestContext.Current.CancellationToken;

        var title = $"Facets-blank-{Guid.NewGuid():N}";
        var externalId = $"ext-{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

            var jobAd = JobAd.Import(
                title: title,
                company: Company.Create("Test Company AB").Value,
                description: "beskrivning",
                url: $"https://example.com/jobs/{externalId}",
                external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
                rawPayload: "{\"id\":\"" + externalId + "\"}",
                // A source that sent blanks rather than omitting the keys — the realistic wire case.
                facets: TestFacets.From(ssyk: "", municipality: "   ", organizationNumber: null),
                publishedAt: clock.UtcNow.AddDays(-1),
                expiresAt: clock.UtcNow.AddDays(30),
                clock: clock).Value;

            db.JobAds.Add(jobAd);
            await db.SaveChangesAsync(ct);
        }

        var row = await ReadFacetsAsync(title, ct);

        row.SsykConceptId.ShouldBeNull(
            "a blank facet must reach Postgres as NULL. An empty string is NOT NULL, so it would sit " +
            "inside ix_job_ads_ssyk_concept_id matching nothing — indexed and invisible.");
        row.MunicipalityConceptId.ShouldBeNull("whitespace is blank too");
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<string> SeedImportedAdAsync(int publishedDaysAgo, CancellationToken ct)
    {
        var title = $"Facets-{Guid.NewGuid():N}";
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // The payload is still stored (ADR 0032 §4 — debug/replay), and it is what the purge deletes.
        // The FACETS are what must outlive it, so they are passed explicitly here rather than derived:
        // this test is ABOUT the facets.
        var rawPayload =
            "{\"id\":\"" + externalId + "\"," +
            "\"occupation\":{\"concept_id\":\"Ssyk_uwa_841\"}," +
            "\"occupation_group\":{\"concept_id\":\"DJh5_yyF_hEM\"}," +
            "\"workplace_address\":{\"region_concept_id\":\"CaRE_1nn_hRb\"," +
            "\"municipality_concept_id\":\"AvNB_uwa_6n6\"}," +
            "\"employment_type\":{\"concept_id\":\"PFZr_Syz_cUq\"}," +
            "\"working_hours_type\":{\"concept_id\":\"6YE1_gAC_R2G\"}," +
            "\"employer\":{\"name\":\"Test Company AB\",\"organization_number\":\"5592804784\"}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.From(
                ssyk: "Ssyk_uwa_841",
                occupationGroup: "DJh5_yyF_hEM",
                municipality: "AvNB_uwa_6n6",
                region: "CaRE_1nn_hRb",
                employmentType: "PFZr_Syz_cUq",
                worktimeExtent: "6YE1_gAC_R2G",
                organizationNumber: "5592804784"),
            publishedAt: clock.UtcNow.AddDays(-publishedDaysAgo),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return title;
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PurgeStaleRawPayloadsJob>();
        await job.RunAsync(ct);
    }

    // Read the raw columns, bypassing EF materialisation entirely — the question is what POSTGRES holds,
    // not what the model would map back. (An EF read could mask a write that never reached the database.)
    private async Task<FacetRow> ReadFacetsAsync(string title, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Database
            .SqlQueryRaw<FacetRow>(
                """
                SELECT ssyk_concept_id,
                       region_concept_id,
                       occupation_group_concept_id,
                       municipality_concept_id,
                       employment_type_concept_id,
                       worktime_extent_concept_id,
                       organization_number,
                       (raw_payload IS NULL) AS raw_payload_is_null
                FROM job_ads
                WHERE title = {0}
                """,
                title)
            .ToListAsync(ct);

        return rows.ShouldHaveSingleItem();
    }
}
