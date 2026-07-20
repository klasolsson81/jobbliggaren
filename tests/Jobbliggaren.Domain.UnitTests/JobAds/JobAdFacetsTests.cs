using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// #841 — the domain invariants of <see cref="JobAdFacets"/> and of the atomic payload write.
/// </summary>
public class JobAdFacetsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private static IDateTimeProvider Clock => new FakeDateTimeProvider(Now);

    // ── The empty-string invariant ───────────────────────────────────────────────────────────────
    //
    // This is the most dangerous NEW bug #841 could have introduced, and it is worth stating plainly.
    // Postgres' `->>'concept_id'` returned SQL NULL for a missing key. A C# parser naturally returns "".
    // And "" IS NOT NULL — so an empty string would be indexed by the partial
    // `WHERE ... IS NOT NULL` indexes and then match no IN (...) list, ever. The row would be present,
    // indexed, and invisible: #841's own failure mode (a value that looks written and is functionally
    // absent), re-entering through the front door of its own fix. The type refuses it.

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   \n  ")]
    public void Blank_facet_is_normalised_to_null(string blank)
    {
        var facets = new JobAdFacets(
            ssykConceptId: blank,
            occupationGroupConceptId: blank,
            municipalityConceptId: blank,
            regionConceptId: blank,
            employmentTypeConceptId: blank,
            worktimeExtentConceptId: blank,
            organizationNumber: blank);

        facets.SsykConceptId.ShouldBeNull(
            "an empty string is NOT NULL in Postgres: it would enter the partial WHERE ... IS NOT NULL " +
            "index and then match nothing, forever, silently. Blank must become null.");
        facets.OccupationGroupConceptId.ShouldBeNull();
        facets.MunicipalityConceptId.ShouldBeNull();
        facets.RegionConceptId.ShouldBeNull();
        facets.EmploymentTypeConceptId.ShouldBeNull();
        facets.WorktimeExtentConceptId.ShouldBeNull();
        facets.OrganizationNumber.ShouldBeNull();
        facets.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_but_the_value_is_kept()
    {
        var facets = new JobAdFacets(
            ssykConceptId: "  Ssyk_uwa_111  ",
            occupationGroupConceptId: null,
            municipalityConceptId: null,
            regionConceptId: null,
            employmentTypeConceptId: null,
            worktimeExtentConceptId: null,
            organizationNumber: "\t5592804784\n");

        // A concept id with stray whitespace would not equal the taxonomy id in an IN (...) list —
        // the index would be used and the row would still never match.
        facets.SsykConceptId.ShouldBe("Ssyk_uwa_111");
        facets.OrganizationNumber.ShouldBe("5592804784");
        facets.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void None_is_the_named_absence()
    {
        JobAdFacets.None.IsEmpty.ShouldBeTrue();
        JobAdFacets.None.SsykConceptId.ShouldBeNull();
        JobAdFacets.None.OrganizationNumber.ShouldBeNull();
    }

    // ── The atomic write ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_writes_the_payload_and_all_seven_facets_together()
    {
        var result = JobAd.Import(
            title: "Systemutvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: "https://example.com/jobs/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: "{\"id\":\"ext-1\"}",
            facets: new JobAdFacets(
                ssykConceptId: "Ssyk_uwa_111",
                occupationGroupConceptId: "DJh5_yyF_hEM",
                municipalityConceptId: "AvNB_uwa_6n6",
                regionConceptId: "CaRE_1nn_hRb",
                employmentTypeConceptId: "PFZr_Syz_cUq",
                worktimeExtentConceptId: "6YE1_gAC_R2G",
                organizationNumber: "5592804784"),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(30),
            clock: Clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None);

        var jobAd = result.Value;

        jobAd.RawPayload.ShouldBe("{\"id\":\"ext-1\"}");
        jobAd.SsykConceptId.ShouldBe("Ssyk_uwa_111");
        jobAd.OccupationGroupConceptId.ShouldBe("DJh5_yyF_hEM");
        jobAd.MunicipalityConceptId.ShouldBe("AvNB_uwa_6n6");
        jobAd.RegionConceptId.ShouldBe("CaRE_1nn_hRb");
        jobAd.EmploymentTypeConceptId.ShouldBe("PFZr_Syz_cUq");
        jobAd.WorktimeExtentConceptId.ShouldBe("6YE1_gAC_R2G");
        jobAd.OrganizationNumber.ShouldBe("5592804784");
    }

    [Fact]
    public void UpdateFromSource_refreshes_the_facets_with_the_payload()
    {
        // The Update path is where the old design leaked: raw_payload was rewritten nightly and Postgres
        // recomputed the seven from it. Now C# must rewrite them — and cannot forget, because the facets
        // are a required parameter. This pins that an ad whose source data CHANGED (moved municipality,
        // re-classified occupation) gets its facets updated rather than keeping the stale ones.
        var jobAd = JobAd.Import(
            title: "Systemutvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: "https://example.com/jobs/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: "{\"v\":1}",
            facets: new JobAdFacets("Ssyk_old", "Grp_old", "Kommun_old", "Lan_old", null, null, "5560000000"),
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(30),
            clock: Clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        var result = jobAd.UpdateFromSource(
            title: "Senior systemutvecklare",
            description: "ny beskrivning",
            url: "https://example.com/jobs/1",
            rawPayload: "{\"v\":2}",
            facets: new JobAdFacets("Ssyk_new", "Grp_new", "Kommun_new", "Lan_new", "Emp_new", "Wt_new", "5592804784"),
            expiresAt: Now.AddDays(60), declaredContacts: [], extractTerms: TestKeywordExtraction.None);

        result.IsSuccess.ShouldBeTrue();
        jobAd.RawPayload.ShouldBe("{\"v\":2}");
        jobAd.SsykConceptId.ShouldBe("Ssyk_new");
        jobAd.OccupationGroupConceptId.ShouldBe("Grp_new");
        jobAd.MunicipalityConceptId.ShouldBe("Kommun_new");
        jobAd.RegionConceptId.ShouldBe("Lan_new");
        jobAd.EmploymentTypeConceptId.ShouldBe("Emp_new");
        jobAd.WorktimeExtentConceptId.ShouldBe("Wt_new");
        jobAd.OrganizationNumber.ShouldBe("5592804784");
    }

    [Fact]
    public void UpdateFromSource_can_clear_a_facet_the_source_stopped_sending()
    {
        // The other direction, and it must not be forgotten: if the source drops a key, the column must
        // go NULL rather than keep a stale value. A partial "refresh only what is present" write would
        // leave the ad indexed under a municipality it no longer has.
        var jobAd = JobAd.Import(
            title: "Systemutvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: "https://example.com/jobs/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: "{\"v\":1}",
            facets: new JobAdFacets("Ssyk_1", null, "Kommun_1", null, null, null, "5560000000"),
            publishedAt: Now.AddDays(-1),
            expiresAt: null,
            clock: Clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        jobAd.UpdateFromSource(
            title: "Systemutvecklare",
            description: "beskrivning",
            url: "https://example.com/jobs/1",
            rawPayload: "{\"v\":2}",
            facets: JobAdFacets.None,
            expiresAt: null, declaredContacts: [], extractTerms: TestKeywordExtraction.None).IsSuccess.ShouldBeTrue();

        jobAd.SsykConceptId.ShouldBeNull("the source stopped sending it — the column must follow");
        jobAd.MunicipalityConceptId.ShouldBeNull();
        jobAd.OrganizationNumber.ShouldBeNull();
    }

    [Fact]
    public void Import_rejects_null_facets()
    {
        Should.Throw<ArgumentNullException>(() => JobAd.Import(
            title: "Systemutvecklare",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: "https://example.com/jobs/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: "{\"id\":\"ext-1\"}",
            facets: null!,
            publishedAt: Now.AddDays(-1),
            expiresAt: null,
            clock: Clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None));
    }

    [Fact]
    public void Create_has_no_source_facets_and_that_is_correct()
    {
        // A manually created ad has no JobTech payload, so it has no source facets. NULL is the TRUE
        // value here, not a gap — and the seven partial indexes are WHERE ... IS NOT NULL precisely
        // because null-sparsity is expected. An invariant demanding facets would be a lie about the
        // domain.
        var jobAd = JobAd.Create(
            title: "Internt uppdrag",
            company: Company.Create("Acme AB").Value,
            description: "beskrivning",
            url: "https://example.com/manual",
            source: JobSource.Manual,
            publishedAt: Now.AddDays(-1),
            expiresAt: Now.AddDays(30),
            clock: Clock).Value;

        jobAd.RawPayload.ShouldBeNull();
        jobAd.SsykConceptId.ShouldBeNull();
        jobAd.OccupationGroupConceptId.ShouldBeNull();
        jobAd.MunicipalityConceptId.ShouldBeNull();
        jobAd.RegionConceptId.ShouldBeNull();
        jobAd.EmploymentTypeConceptId.ShouldBeNull();
        jobAd.WorktimeExtentConceptId.ShouldBeNull();
        jobAd.OrganizationNumber.ShouldBeNull();
    }
}
