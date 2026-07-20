using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// #300 CAPSTONE (PR-5a end-to-end) — proves the WHOLE Related chain on the wire: a real
/// substitutability edge from the startup-seeded snapshot → exact→related broadening when
/// <c>includeRelated=true</c> → grade <see cref="MatchGrade.Related"/> → it surfaces on the
/// batch overlay, the modal detail, AND the list filter/sort; and is ABSENT when the flag is
/// off (exact-only). NO AI/LLM (ADR 0071/0084).
/// <para>
/// <b>Why a SNAPSHOT edge, not a per-test seeded <c>taxonomy_relations</c> row (the cache-race
/// fix, Klas 2026-06-28):</b> <c>TaxonomyReadModel</c> is a process-wide singleton that loads
/// <c>taxonomy_relations</c> ONCE per host (success-only lazy cache). A per-test edge would race
/// the cache warm in the shared <c>[Collection("Api")]</c> host. Instead we use an edge that is
/// in the table from host-START — <c>ApiFactory.InitializeAsync</c> runs
/// <c>TaxonomySnapshotSeeder</c>, which writes the committed
/// <c>occupation-substitutability.json</c> edges — so whichever test warms the cache reads it.
/// </para>
/// <para>
/// <b>The verified edge</b> (<c>occupation-substitutability.json</c>): source
/// <c>13md_uyV_BNG</c> ("Drifttekniker, IT") → related incl. <c>VCpu_5EN_bBt</c>
/// ("Nätverks- och systemtekniker m.fl."). Both are real OccupationGroup concepts in
/// <c>taxonomy-snapshot.json</c> (so the grade-WHERE/seeder resolve them). The seeker states
/// ONLY the exact source; the ad's occupation-group shadow is the related-only target —
/// disjoint from the exact set by construction (the seeker never states <c>VCpu_5EN_bBt</c>,
/// and <c>GetRelatedOccupationGroupsAsync</c> excludes any exact member from the related set).
/// </para>
/// </summary>
[Collection("Api")]
public class RelatedSurfacingEndToEndTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // The verified snapshot substitutability edge (exact source → related-only target).
    private const string ExactSourceGroup = "13md_uyV_BNG";  // Drifttekniker, IT (seeker states this)
    private const string RelatedOnlyGroup = "VCpu_5EN_bBt";  // Nätverks- och systemtekniker m.fl. (ad's group)

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // States the seeker's exact occupation (the SSYK-gate source). Region/employment left empty
    // — the flat Related cap (ADR 0084 §F2) makes secondaries irrelevant to the Related grade.
    private Task SetExactOccupationAsync(CancellationToken ct) =>
        SetExactOccupationAsync(ExactSourceGroup, ct);

    // Parameterized variant (#379): states an arbitrary exact occupation group. Mirrors the
    // default helper exactly — only the group concept-id varies.
    private async Task SetExactOccupationAsync(string occupationGroup, CancellationToken ct)
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            new
            {
                preferredOccupationGroups = new[] { occupationGroup },
                preferredRegions = Array.Empty<string>(),
                preferredEmploymentTypes = Array.Empty<string>(),
                preferredSkills = Array.Empty<string>(),
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // Seeds an Imported JobAd whose raw_payload drives the STORED occupation_group shadow column
    // to the related-only group (parity MatchTagBatchEndpointsTests.SeedJobAdAsync). Region/
    // employment null → those shadows NULL (irrelevant under the flat Related cap).
    private Task<Guid> SeedRelatedOnlyAdAsync(string title, CancellationToken ct) =>
        SeedAdInOccupationGroupAsync(title, RelatedOnlyGroup, ct);

    // Parameterized seeder (#379): seeds an Imported JobAd whose STORED occupation_group shadow
    // column is the GIVEN concept-id. Mirrors SeedRelatedOnlyAdAsync exactly — only the
    // occupation-group varies — so a Data/IT-misclassified ad (occupation_group = MYAz_x9m_2LJ)
    // and a genuine Administration-field ad (occupation_group = eQ4M_CNm_ozj) can be seeded.
    private async Task<Guid> SeedAdInOccupationGroupAsync(
        string title, string occupationGroup, CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}},"
            + "\"workplace_address\":null,"
            + "\"employment_type\":null}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // ---- batch overlay wire helpers (parity MatchTagBatchEndpointsTests) ----
    private async Task<JsonElement> PostMatchTagsAsync(Guid[] ids, bool? includeRelated, CancellationToken ct)
    {
        object body = includeRelated is null
            ? new { jobAdIds = ids }
            : new { jobAdIds = ids, includeRelated = includeRelated.Value };
        var response = await _client.PostAsJsonAsync("/api/v1/me/job-ad-match-tags", body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static bool TryGetEntry(JsonElement dto, Guid id, out JsonElement entry)
    {
        entry = default;
        return dto.TryGetProperty("entries", out var entries)
            && entries.TryGetProperty(id.ToString(), out entry);
    }

    private static string Wire(MatchGrade grade) => grade.ToString();

    // =================================================================
    // 1. POST /me/job-ad-match-tags — includeRelated:true tags the related-only ad with
    //    grade "Related"; false (and omitted) → the ad is ABSENT (exact-only gate fails).
    // =================================================================

    [Fact]
    public async Task POST_match_tags_includeRelated_true_tags_related_ad_and_false_omits_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SetExactOccupationAsync(ct);

        var relatedAd = await SeedRelatedOnlyAdAsync("Nätverkstekniker", ct);

        // includeRelated:true → broadening opens the related rung → grade Related.
        var withRelated = await PostMatchTagsAsync([relatedAd], includeRelated: true, ct);
        TryGetEntry(withRelated, relatedAd, out var entry).ShouldBeTrue(
            "Med includeRelated:true ska den related-only-annonsen tagga:s (broadening öppnar related-rungen).");
        entry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Related),
            "Den related-only-annonsen ska graderas Related (flat cap, ADR 0084 §F2) på tråden.");

        // includeRelated:false → exact-only → SSYK-gaten faller → annonsen frånvarande.
        var noRelatedFalse = await PostMatchTagsAsync([relatedAd], includeRelated: false, ct);
        TryGetEntry(noRelatedFalse, relatedAd, out _).ShouldBeFalse(
            "Med includeRelated:false ska annonsen vara FRÅNVARANDE (exakt-only, gate fails).");

        // Omitted → defaults to false → also absent (the default-off behaviour on the wire).
        var noRelatedOmitted = await PostMatchTagsAsync([relatedAd], includeRelated: null, ct);
        TryGetEntry(noRelatedOmitted, relatedAd, out _).ShouldBeFalse(
            "Utan includeRelated (default false) ska annonsen vara FRÅNVARANDE — exakt-only.");
    }

    // =================================================================
    // 2. GET /me/job-ad-match-tags/{id} — includeRelated=true → grade "Related"; without the
    //    param → grade null (the modal renders the honest breakdown, grade null = no tag).
    // =================================================================

    [Fact]
    public async Task GET_match_detail_includeRelated_true_grades_related_and_without_param_grade_is_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SetExactOccupationAsync(ct);

        var relatedAd = await SeedRelatedOnlyAdAsync("Systemtekniker", ct);

        // includeRelated=true → broadened → grade Related.
        var withResp = await _client.GetAsync(
            $"/api/v1/me/job-ad-match-tags/{relatedAd}?includeRelated=true", ct);
        withResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withDto = await withResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        withDto.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Related),
            "Med includeRelated=true ska modal-detaljens grad vara Related på tråden.");

        // Without the param (default false) → exact-only → no tag → grade null. The modal still
        // returns a non-null DTO (honest breakdown), the GRADE is null.
        var withoutResp = await _client.GetAsync(
            $"/api/v1/me/job-ad-match-tags/{relatedAd}", ct);
        withoutResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withoutDto = await withoutResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        withoutDto.GetProperty("grade").ValueKind.ShouldBe(JsonValueKind.Null,
            "Utan includeRelated (exakt-only) ska graden vara null — annonsen taggas inte.");
    }

    // =================================================================
    // 3. GET /api/v1/job-ads?matchGrades=Related&includeRelated=true&sortBy=MatchDesc — the
    //    related ad appears (graded Related); with includeRelated=false the related set is empty
    //    → nothing is tagged Related → the related ad is absent from the page.
    // =================================================================

    [Fact]
    public async Task GET_job_ads_related_filter_with_includeRelated_true_returns_ad_and_false_omits_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        await SetExactOccupationAsync(ct);

        var relatedAd = await SeedRelatedOnlyAdAsync("Drifttekniker IT (related-yta)", ct);

        // includeRelated=true + matchGrades=Related → the related ad is in the page.
        var withResp = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=Related&includeRelated=true&sortBy=MatchDesc&pageSize=100", ct);
        withResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withDto = await withResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var withIds = withDto.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        withIds.ShouldContain(relatedAd,
            "Med includeRelated=true ska {Related}-filtret returnera den related-only-annonsen " +
            "(broadening → grad Related → grad-WHERE selekterar den).");

        // includeRelated=false → the related set is empty → NOTHING is graded Related → the
        // {Related} filter cannot return this ad.
        var withoutResp = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=Related&includeRelated=false&sortBy=MatchDesc&pageSize=100", ct);
        withoutResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var withoutDto = await withoutResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var withoutIds = withoutDto.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        withoutIds.ShouldNotContain(relatedAd,
            "Med includeRelated=false är related-mängden tom → inget taggas Related → " +
            "{Related}-filtret kan inte returnera annonsen.");
    }

    // =================================================================
    // 4. #379 — SAME-FIELD surfacing guard on the wire. ROOT CAUSE: a "Administratör"
    //    (Randstad) ad surfaces as Related for a Data/IT profile ONLY because JobTech/AF
    //    misclassified that office role into the Data/IT ssyk-4 group "Systemadministratörer"
    //    (MYAz_x9m_2LJ) — a legitimate same-field substitutability edge of DJh5_yyF_hEM after
    //    #359. The engine is CORRECT. This test pins BOTH halves of the invariant end-to-end:
    //    a Data/IT-misclassified ad (occupation_group = MYAz) IS tagged Related (intended), while
    //    a GENUINE Administration-field office-assistant ad (occupation_group = eQ4M_CNm_ozj) is
    //    NEVER tagged Related — the #359 same-field gate drops the cross-field edge. This is the
    //    "Om bugg" guard so the phantom never re-opens.
    //
    //    Both MYAz_x9m_2LJ and eQ4M_CNm_ozj are committed snapshot edge SOURCES already loaded by
    //    ApiFactory's startup seeder, so the process-singleton TaxonomyReadModel cache resolves
    //    them (parity the cache-race note above — no per-test taxonomy_relations row).
    // =================================================================

    // Data/IT profile source (seeker states this) — "Mjukvaru- och systemutvecklare m.fl.".
    private const string DataItProfileGroup = "DJh5_yyF_hEM";
    // Same-field (Data/IT) related group — the mislabeled Randstad "Administratör" ad lands here.
    private const string MislabeledAdminGroup = "MYAz_x9m_2LJ";   // Systemadministratörer — Related is intended
    // Genuine Administration-field office-assistant group — must NEVER be Related to Data/IT.
    private const string AdministrationFieldGroup = "eQ4M_CNm_ozj"; // cross-field — dropped by #359

    [Fact]
    public async Task POST_match_tags_DataItProfile_tags_MYAz_ad_Related_but_not_Administration_field_ad_Issue379()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Seeker is a Data/IT profile (states DJh5_yyF_hEM as the exact occupation group).
        await SetExactOccupationAsync(DataItProfileGroup, ct);

        // (a) The mislabeled-Administratör scenario: an ad upstream-classified into the Data/IT
        //     ssyk-4 group Systemadministratörer (MYAz) — same-field related to the profile.
        var mislabeledAdminAd = await SeedAdInOccupationGroupAsync(
            "Administratör (Systemadministratörer-klassad)", MislabeledAdminGroup, ct);

        // (b) A genuine Administration-field office-assistant ad (eQ4M) — cross-field.
        var administrationFieldAd = await SeedAdInOccupationGroupAsync(
            "Administrativ assistent (kontor)", AdministrationFieldGroup, ct);

        var dto = await PostMatchTagsAsync(
            [mislabeledAdminAd, administrationFieldAd], includeRelated: true, ct);

        // The MYAz ad MUST be tagged Related — Systemadministratörer is a CORRECT same-field
        // (Data/IT) related group of the profile. This is the intended Randstad-ad mechanism (#379).
        TryGetEntry(dto, mislabeledAdminAd, out var mislabeledEntry).ShouldBeTrue(
            "Den Data/IT-klassade (MYAz) annonsen ska taggas — Systemadministratörer är same-field " +
            "related till Mjukvaru- och systemutvecklare. Surfacing är AVSIKTLIGT (#379).");
        mislabeledEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Related),
            "MYAz-annonsen ska graderas Related (same-field substitutability efter #359).");

        // The eQ4M (Administration-field) ad MUST be ABSENT — the #359 same-field gate drops the
        // cross-field edge. A Data/IT profile NEVER surfaces an Administration-FIELD ad as Related.
        TryGetEntry(dto, administrationFieldAd, out _).ShouldBeFalse(
            "Den Administration-fält-annonsen (eQ4M_CNm_ozj) ska vara FRÅNVARANDE — #359 same-field-" +
            "gaten droppar cross-field-edgen. En Data/IT-profil ytar ALDRIG en Administration-fält-" +
            "annons som Related (#379 'Om bugg'-guard).");
    }
}
