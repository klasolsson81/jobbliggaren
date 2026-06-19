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
/// <c>PUT /me/match-preferences</c> endpoint, then seed JobAds whose STORED shadow columns
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

    // Seeds an Imported JobAd whose raw_payload drives the STORED shadow columns
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
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    private async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAdId = new JobAdId(id);
        var ad = await db.JobAds.FindAsync([jobAdId], ct);
        ad.ShouldNotBeNull();
        db.Entry(ad!).Property(nameof(JobAd.DeletedAt)).CurrentValue = clock.UtcNow;
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
    // EF translation oracle — authed user with preferences gets graded entries
    // for the matching ads (proves the `= ANY` query translates on real Postgres)
    // =================================================================

    [Fact]
    public async Task POST_match_tags_returns_graded_entries_for_matching_ads()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        var reg = NewConceptId("reg");
        var emp = NewConceptId("emp");
        await SetPreferencesAsync([grp], [reg], [emp], ct);

        // strongAd: occupation + both secondaries confirmed → Strong.
        var strongAd = await SeedJobAdAsync("Systemutvecklare", grp, reg, emp, ct);
        // goodAd: occupation + region confirmed, employment NULL (NotAssessed) → Good.
        var goodAd = await SeedJobAdAsync("Arkitekt", grp, reg, null, ct);
        // basicAd: occupation only, both secondaries NULL → Basic.
        var basicAd = await SeedJobAdAsync("Projektledare", grp, null, null, ct);
        // gatedAd: different occupation (no SSYK match) → omitted (no tag).
        var gatedAd = await SeedJobAdAsync("Sjuksköterska", NewConceptId("grp"), reg, emp, ct);

        var dto = await PostMatchTagsAsync([strongAd, goodAd, basicAd, gatedAd], ct);

        // Only the three occupation-matching ads earn a tag; the gated ad is absent.
        EntriesCount(dto).ShouldBe(3);

        TryGetEntry(dto, strongAd, out var strongEntry).ShouldBeTrue();
        strongEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Strong));
        strongEntry.GetProperty("ssykOverlap").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        strongEntry.GetProperty("regionFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        strongEntry.GetProperty("employmentFit").GetString().ShouldBe(Wire(MatchDimensionVerdict.Match));
        // Title is always NotAssessed on the preference path (no CV title until F4-15).
        strongEntry.GetProperty("titleSimilarity").GetString().ShouldBe(Wire(MatchDimensionVerdict.NotAssessed));

        TryGetEntry(dto, goodAd, out var goodEntry).ShouldBeTrue();
        goodEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Good));

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
    // A soft-deleted ad id + a non-existent id are OMITTED (no throw)
    // =================================================================

    [Fact]
    public async Task POST_match_tags_omits_soft_deleted_and_non_existent_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var grp = NewConceptId("grp");
        await SetPreferencesAsync([grp], [], [], ct);

        var liveAd = await SeedJobAdAsync("Systemutvecklare", grp, null, null, ct);
        var deletedAd = await SeedJobAdAsync("Arkitekt", grp, null, null, ct);
        await SoftDeleteAsync(deletedAd, ct);
        var ghost = Guid.NewGuid(); // never seeded

        var dto = await PostMatchTagsAsync([liveAd, deletedAd, ghost], ct);

        // Only the live ad earns an entry; soft-deleted + non-existent are silently absent.
        EntriesCount(dto).ShouldBe(1);
        TryGetEntry(dto, liveAd, out var liveEntry).ShouldBeTrue();
        liveEntry.GetProperty("grade").GetString().ShouldBe(Wire(MatchGrade.Basic));
        TryGetEntry(dto, deletedAd, out _).ShouldBeFalse();
        TryGetEntry(dto, ghost, out _).ShouldBeFalse();
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
