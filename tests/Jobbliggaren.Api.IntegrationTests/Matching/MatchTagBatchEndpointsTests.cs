using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 A1/B2/C2a) — the page-scoped
/// match-tag batch-overlay endpoint <c>POST /api/v1/me/job-ad-match-tags</c> end-to-end on
/// Testcontainers Postgres. This is the ORACLE for the EF <c>= ANY</c> translation in
/// <c>ScoreBatchAsync</c> (InMemory hides it) wired through the full Mediator pipeline +
/// the real preference→profile→grade path. Parity <c>JobAdStatusEndpointsTests</c> +
/// <c>MatchScorerIntegrationTests</c> seeding.
/// <para>
/// A user registered via <c>RegisterAndGetSessionIdAsync</c> already HAS a JobSeeker
/// (RegisterCommandHandler creates it). We state their match preferences via the existing
/// <c>PUT /me/match-preferences</c> endpoint, then seed JobAds whose facet columns
/// (occupation_group / region / employment, Postgres generated columns derived from
/// raw_payload) carry the SAME concept-ids — so the deterministic ladder produces a known
/// grade per ad.
/// </para>
/// </summary>
[Collection("Api")]
public class MatchTagBatchEndpointsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SetPreferencesAsync(
        string[] occupationGroups, string[] regions, string[] employmentTypes,
        CancellationToken ct)
    {
        var response = await _client.PutAsJsonAsync(
            "/api/v1/me/match-preferences",
            new
            {
                preferredOccupationGroups = occupationGroups,
                preferredRegions = regions,
                preferredEmploymentTypes = employmentTypes,
            },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<JsonElement> PostMatchTagsAsync(Guid[] ids, CancellationToken ct)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-match-tags",
            new { jobAdIds = ids },
            ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    // Seeds an Imported JobAd whose raw_payload drives the facet columns
    // (parity MatchScorerIntegrationTests.SeedJobAdAsync). null → shadow NULL.
    private async Task<Guid> SeedJobAdAsync(
        string title,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var rawPayload = BuildRawPayload(
            externalId, occupationGroupConceptId, regionConceptId, employmentTypeConceptId);

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

    // The REAL retraction transition (#821: Archive() is JobAd's only lifecycle method -
    // there is no soft-delete axis to stamp).
    private async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAdId = new JobAdId(id);
        var ad = await db.JobAds.FindAsync([jobAdId], ct);
        ad.ShouldNotBeNull();
        ad!.Archive(clock);
        await db.SaveChangesAsync(ct);
    }

    private static string BuildRawPayload(
        string externalId,
        string? occupationGroupConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId)
    {
        var groupJson = occupationGroupConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{occupationGroupConceptId}\"}}";
        var addressJson = regionConceptId is null
            ? "null"
            : $"{{\"region_concept_id\":\"{regionConceptId}\"}}";
        var employmentJson = employmentTypeConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{employmentTypeConceptId}\"}}";

        return
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{groupJson},"
            + $"\"workplace_address\":{addressJson},"
            + $"\"employment_type\":{employmentJson}}}";
    }

    // Unique-but-regex-valid concept-id (^[A-Za-z0-9_-]{1,32}$). 16 chars.
    private static string NewConceptId(string prefix) =>
        $"{prefix}{Guid.NewGuid():N}"[..16];

    private static bool TryGetEntry(JsonElement dto, Guid id, out JsonElement entry)
    {
        entry = default;
        if (!dto.TryGetProperty("entries", out var entries))
            return false;
        return entries.TryGetProperty(id.ToString(), out entry);
    }

    private static int EntriesCount(JsonElement dto) =>
        dto.GetProperty("entries").EnumerateObject().Count();

    // MatchGrade + MatchDimensionVerdict carry [JsonStringEnumConverter] (F4-13 is the
    // first surface to put them on the wire), so they serialize as their NAME
    // ("Strong"/"Good"/"Basic", "Match"/"NotAssessed", ...), never an ordinal. We assert
    // against the LIVE enum name so a future rename is caught here without magic strings.
    private static string Wire(MatchGrade grade) => grade.ToString();
    private static string Wire(MatchDimensionVerdict verdict) => verdict.ToString();

    // =================================================================
    // EF translation oracle — authed user with preferences but NO CV gets graded
    // entries for the matching ads (proves the `= ANY` query translates on real
    // Postgres). PR-B1 (RE-BIND G1-a): the requirement-aware grade CAPS at Good without
    // a CV — must-have coverage is NotAssessed (no resolved CV skills) → the gate is not
    // met → an occupation+both-secondaries ad is Good, NOT Strong. This is the
    // load-bearing no-CV ceiling reversal on the wire.
    // =================================================================

    [Fact]
    public async Task POST_match_tags_capsAtGood_withoutCv_forMatchingAds()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");
        await SetPreferencesAsync([grp], [reg], [emp], ct);

        // ceilingAd: occupation + both secondaries confirmed, but NO CV → must-have
        // NotAssessed → gate NOT met → caps at Good (PR-B1 reversal; pre-rebind = Strong).
        var ceilingAd = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, ct);
        // flooredAd: occupation + region confirmed, employment NULL vs the STATED emp
        // preference → NoMatch → RB1-floored to Basic (#552 gate; pre-gate this shape
        // read NotAssessed → Good — the Good rung now lives in the containment oracles).
        var flooredAd = await SeedJobAdAsync("Arkitekt", grp, reg, null, ct);
        // basicAd: occupation only, both secondaries NULL vs stated prefs → floored Basic
        // (#552 gate; pre-gate the same Basic via zero confirmed secondaries).
        var basicAd = await SeedJobAdAsync("Projektledare", grp, null, null, ct);
        // gatedAd: different occupation (no SSYK match) → omitted (no tag).
        var gatedAd = await SeedJobAdAsync("Sjuksköterska", NewConceptId("grp"), reg, emp, ct);

        var dto = await PostMatchTagsAsync([ceilingAd, flooredAd, basicAd, gatedAd], ct);

        // Only the three occupation-matching ads earn a tag; the gated ad is absent.
        EntriesCount(dto).ShouldBe(3);

        TryGetEntry(dto, ceilingAd, out var ceilingEntry).ShouldBeTrue();
        // No CV → must-have gate not met → Good ceiling, never Strong (the reversal).
        ceilingEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Good));
        ceilingEntry.GetProperty("ssykOverlap").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        ceilingEntry.GetProperty("regionFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        ceilingEntry.GetProperty("employmentFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        // No CV → must-have coverage NotAssessed (no resolved CV skills to assess against).
        ceilingEntry.GetProperty("mustHaveCoverage").GetString().ShouldBe(Wire(MatchDimensionVerdict.NotAssessed));
        // Title is always NotAssessed on the preference path.
        ceilingEntry.GetProperty("titleSimilarity").GetString().ShouldBe(Wire(MatchDimensionVerdict.NotAssessed));

        TryGetEntry(dto, flooredAd, out var flooredEntry).ShouldBeTrue();
        // #552: the ad is silent on the stated employment dimension → NoMatch → Basic.
        flooredEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));
        flooredEntry.GetProperty("employmentFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.NoMatch));

        TryGetEntry(dto, basicAd, out var basicEntry).ShouldBeTrue();
        basicEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));

        TryGetEntry(dto, gatedAd, out _).ShouldBeFalse();
    }

    // =================================================================
    // Region-gate end-to-end — an ad in a region the user did NOT state
    // (stated some other region) floors the grade to Basic
    // =================================================================

    [Fact]
    public async Task POST_match_tags_floors_grade_to_basic_when_region_contradicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        var statedRegion = NewConceptId("reg");
        var otherRegion = NewConceptId("reg");
        var emp = NewConceptId("emp");
        // User states grp + statedRegion + emp.
        await SetPreferencesAsync([grp], [statedRegion], [emp], ct);

        // Ad matches occupation + employment, but is in a region the user did NOT state
        // (a contradicted preference) → the ladder floors it to Basic even though
        // employment is confirmed.
        var contradictedAd = await SeedJobAdAsync(
            "Systemutvecklare", grp, otherRegion, emp, ct);

        var dto = await PostMatchTagsAsync([contradictedAd], ct);

        TryGetEntry(dto, contradictedAd, out var entry).ShouldBeTrue();
        entry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));
        entry.GetProperty("regionFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.NoMatch));
        entry.GetProperty("employmentFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
    }

    // =================================================================
    // SPECIFICATION (#864) - a non-existent id AND an archived ad are both OMITTED (no throw)
    //
    // This was a CHARACTERIZATION test (Feathers 2004, ch. 13) asserting that an archived ad WAS
    // tagged here, with the note: "when #864 lands, this flips to ShouldBeFalse." #864 landed
    // (CTO D2, S-split); it has flipped. This endpoint is where the gap was EXPOSED - the id list
    // is client-supplied, so a caller could ask for, and receive, a real match grade for an ad the
    // product may no longer present. The port now defines "missing" as: the row does not exist OR
    // the ad is not Active. Both are silently omitted - one stale id must never fail a page render.
    //
    // ASYMMETRIC SEED (2 live + 1 archived + 1 ghost): with a single live ad, EntriesCount reads 1
    // under BOTH the correct gate and an INVERTED one (`== Archived`) - blind to polarity. With two,
    // the states separate: correct → 2, gate deleted → 3, gate inverted → 1.
    // =================================================================
    [Fact]
    public async Task POST_match_tags_omits_both_non_existent_ids_and_archived_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        await SetPreferencesAsync([grp], [], [], ct);

        var liveAd = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var liveAd2 = await SeedJobAdAsync("Backendutvecklare", grp, null, null, ct);
        var archivedAd = await SeedJobAdAsync("Arkitekt", grp, null, null, ct);
        await ArchiveAsync(archivedAd, ct);
        var ghost = Guid.NewGuid(); // never seeded

        var dto = await PostMatchTagsAsync([liveAd, liveAd2, archivedAd, ghost], ct);

        // REAL, unchanged coverage: a non-existent id is silently absent (no 404, no faked tag).
        TryGetEntry(dto, ghost, out _).ShouldBeFalse();

        // NON-VACUITY FIRST (#841): the ACTIVE ads ARE tagged, with a real grade. Without this,
        // "the archived one is absent" would pass trivially the day the endpoint tags nothing.
        TryGetEntry(dto, liveAd, out var liveEntry).ShouldBeTrue();
        liveEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));
        TryGetEntry(dto, liveAd2, out var liveEntry2).ShouldBeTrue();
        liveEntry2.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));

        // THE SPECIFICATION: the archived ad is omitted exactly like the ghost. A client cannot
        // obtain a match grade for an ad the product may no longer present, by asking for its id.
        TryGetEntry(dto, archivedAd, out _).ShouldBeFalse(
            "the batch scorer gates on Status == Active (#864): an archived ad is MISSING to the " +
            "batch family, so this endpoint omits it exactly like a non-existent id.");

        // Polarity: 2, not 1 (inverted gate) and not 3 (gate deleted).
        EntriesCount(dto).ShouldBe(2);
    }

    // =================================================================
    // Anonymous (no auth) → 200 with empty entries (no 401 friction)
    // =================================================================

    [Fact]
    public async Task POST_match_tags_without_auth_returns_200_with_empty_entries()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header — anonymous-tolerant per ADR 0063 §Kontext.
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-match-tags",
            new { jobAdIds = new[] { Guid.NewGuid() } },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        EntriesCount(dto).ShouldBe(0);
    }

    // =================================================================
    // Authed user with NO preferences → empty entries (the occupation gate)
    // =================================================================

    [Fact]
    public async Task POST_match_tags_with_no_preferences_returns_empty_entries()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // A freshly-registered user has an empty MatchPreferences (no stated occupation).

        var grp = NewConceptId("grp");
        var ad = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);

        var dto = await PostMatchTagsAsync([ad], ct);

        EntriesCount(dto).ShouldBe(0);
    }

    // =================================================================
    // > 100 ids → 400 (validator-cap, MaxJobAdIdsPerCall = 100)
    // =================================================================

    [Fact]
    public async Task POST_match_tags_over_100_ids_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-match-tags",
            new { jobAdIds = ids },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
