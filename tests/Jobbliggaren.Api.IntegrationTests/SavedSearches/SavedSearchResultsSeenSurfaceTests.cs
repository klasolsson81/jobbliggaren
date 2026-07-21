using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.SavedSearches;

/// <summary>
/// #312 (ADR 0115) — the composed in-app "N nya träffar"-surface end-to-end through the HTTP pipeline
/// against real Postgres (Testcontainers). The unit tests fake the port on EF-InMemory and the port
/// Testcontainers test bypasses the handler; THIS class pins the whole loop — GET /new-results-count
/// → the composed read-side (handler → real IJobAdSearchQuery → SearchCriteriaMapping over the
/// persisted VO → DTO serialization) → POST /{id}/results-seen persisting the watermark → the count
/// dropping. It is the #312 sibling of MyMatchesSurfaceTests (the match-rail round-trip).
/// <para>
/// created_at / results_seen_at / updated_at are stamped via raw SQL after seeding so the window is
/// deterministic (the DI clock is not per-seed controllable). Searches are keyed on a unique per-run
/// occupation-group (no q → the count matches purely on the facet), and created through the real POST
/// endpoint (concept-id passes the create validator's <c>^[A-Za-z0-9_-]{1,32}</c> pattern).
/// </para>
/// </summary>
[Collection("Api")]
public class SavedSearchResultsSeenSurfaceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<HttpClient> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    // Creates a notification-enabled saved search keyed only on `runKey` (no q → deterministic
    // facet-only match). sortBy numeric (no JsonStringEnumConverter for body binding).
    private static async Task<string> CreateNotifySearchAsync(
        HttpClient client, string runKey, CancellationToken ct)
    {
        var body = new
        {
            name = "Ytan",
            occupationGroup = new[] { runKey },
            municipality = (string[]?)null,
            region = (string[]?)null,
            q = (string?)null,
            sortBy = 0,
            notificationEnabled = true,
        };
        var response = await client.PostAsJsonAsync("/api/v1/saved-searches", body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    private async Task<JobAdId> SeedActiveAdAsync(string runKey, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"surface-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"occupation_group\":{{\"concept_id\":\"{runKey}\"}}}}";
        var jobAd = JobAd.Import(
            title: "Aktiv roll",
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
        return jobAd.Id;
    }

    private async Task ExecAsync(string sql, CancellationToken ct, params object[] args)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, args, ct);
    }

    private Task SetAdCreatedAtAsync(JobAdId id, DateTimeOffset when, CancellationToken ct) =>
        ExecAsync("UPDATE job_ads SET created_at = {0} WHERE id = {1}", ct, when, id.Value);

    private Task SetSearchWatermarkAsync(string searchId, DateTimeOffset when, CancellationToken ct) =>
        ExecAsync("UPDATE saved_searches SET results_seen_at = {0} WHERE id = {1}", ct, when, Guid.Parse(searchId));

    private Task NullSearchWatermarkAsync(string searchId, CancellationToken ct) =>
        ExecAsync("UPDATE saved_searches SET results_seen_at = NULL WHERE id = {0}", ct, Guid.Parse(searchId));

    private Task SetSearchCreatedAtAsync(string searchId, DateTimeOffset when, CancellationToken ct) =>
        ExecAsync("UPDATE saved_searches SET created_at = {0} WHERE id = {1}", ct, when, Guid.Parse(searchId));

    private Task SetSearchUpdatedAtAsync(string searchId, DateTimeOffset when, CancellationToken ct) =>
        ExecAsync("UPDATE saved_searches SET updated_at = {0} WHERE id = {1}", ct, when, Guid.Parse(searchId));

    private static async Task<int> GetCountForAsync(HttpClient client, string searchId, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/v1/saved-searches/new-results-count", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        var dto = json.EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("savedSearchId").GetString() == searchId);
        dto.ValueKind.ShouldNotBe(JsonValueKind.Undefined, $"sökning {searchId} ska ingå i räkningen");
        return dto.GetProperty("newCount").GetInt32();
    }

    [Fact]
    public async Task RoundTrip_CountReflectsNewActiveAds_ResetsOnSeen_ReappearsOnNewAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = $"grp{Guid.NewGuid():N}"[..16];
        var client = await RegisterUserAsync("ss-surface", ct);
        var searchId = await CreateNotifySearchAsync(client, runKey, ct);

        // Pin the watermark to a fixed past instant so the window boundary is deterministic.
        var watermark = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await SetSearchWatermarkAsync(searchId, watermark, ct);

        // Two active ads ingested AFTER the watermark → both are "new".
        var ad1 = await SeedActiveAdAsync(runKey, ct);
        var ad2 = await SeedActiveAdAsync(runKey, ct);
        await SetAdCreatedAtAsync(ad1, watermark.AddDays(10), ct);
        await SetAdCreatedAtAsync(ad2, watermark.AddDays(11), ct);

        (await GetCountForAsync(client, searchId, ct)).ShouldBe(2);

        // Acknowledge through the window → the count resets. seenThrough is in the past (< clock-now)
        // so it is not clamped; the aggregate advances the persisted watermark past both ads.
        var seenThrough = watermark.AddDays(20);
        var seenResp = await client.PostAsJsonAsync(
            $"/api/v1/saved-searches/{searchId}/results-seen", new { seenThrough }, ct);
        seenResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetCountForAsync(client, searchId, ct)).ShouldBe(0);

        // A newer ad ingested after the acknowledge reappears — the live windowed model is not a
        // spent counter (churn-immunity, the DTO's headline claim, proven through the composed path).
        var ad3 = await SeedActiveAdAsync(runKey, ct);
        await SetAdCreatedAtAsync(ad3, watermark.AddDays(25), ct);

        (await GetCountForAsync(client, searchId, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Count_caps_at_the_20_most_recently_updated_searches()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterUserAsync("ss-cap", ct);

        // 21 notification-enabled searches; stamp distinct updated_at so the cap ordering is
        // deterministic (a mutation OrderByDescending→OrderBy would drop a DIFFERENT id and fail).
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = new List<string>();
        for (var i = 0; i < 21; i++)
        {
            var id = await CreateNotifySearchAsync(client, $"grp{Guid.NewGuid():N}"[..16], ct);
            await SetSearchUpdatedAtAsync(id, baseTime.AddDays(i), ct);
            ids.Add(id);
        }
        var oldest = ids[0];   // updated_at = 2026-01-01 — the least-recently-updated

        var response = await client.GetAsync("/api/v1/saved-searches/new-results-count", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var returned = json.EnumerateArray()
            .Select(e => e.GetProperty("savedSearchId").GetString()).ToList();

        returned.Count.ShouldBe(20);
        returned.ShouldNotContain(oldest,
            "cap:en behåller de 20 mest nyligen uppdaterade — den äldsta droppas (R1/DoS-bound)");
    }

    [Fact]
    public async Task Null_watermark_coalesces_to_the_search_CreatedAt_not_epoch()
    {
        var ct = TestContext.Current.CancellationToken;
        var runKey = $"grp{Guid.NewGuid():N}"[..16];
        var client = await RegisterUserAsync("ss-null", ct);
        var searchId = await CreateNotifySearchAsync(client, runKey, ct);

        // Force the (unreachable-by-construction — ctor + backfill) null-watermark branch: a row as if
        // it predated the #312 backfill. Pin its created_at, then null the watermark.
        var created = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await SetSearchCreatedAtAsync(searchId, created, ct);
        await NullSearchWatermarkAsync(searchId, ct);

        // One ad BEFORE created (would count under an epoch fallback), one AFTER (counts under CreatedAt).
        var before = await SeedActiveAdAsync(runKey, ct);
        var after = await SeedActiveAdAsync(runKey, ct);
        await SetAdCreatedAtAsync(before, created.AddDays(-10), ct);
        await SetAdCreatedAtAsync(after, created.AddDays(10), ct);

        // Fallback is the search's CreatedAt (never epoch): only the AFTER ad counts. An epoch
        // fallback would count both — resurrecting exactly the historical backlog the design suppresses.
        (await GetCountForAsync(client, searchId, ct)).ShouldBe(1);
    }
}
