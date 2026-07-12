using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — end-to-end against Testcontainers Postgres for
/// <c>GET /api/v1/me/followed-company-ads/new-count</c> (the Översikt follow-rail count) and
/// <c>POST /api/v1/me/followed-company-ads/seen</c> (the watermark advance). Real wired stack, NEVER
/// EF-InMemory: only real Postgres proves the hit↔active-watch equijoin on the value-converted
/// <c>CompanyWatchId</c> translates to SQL (InMemory joins in memory and hides a translation failure).
/// <para>
/// Proves the COMMON path (no OnlyMatched filter — the only path until F4): status-agnostic counting,
/// the watermark boundary, the unfollowed-watch exclusion, auth, and cross-user isolation. The
/// grade-filter (OnlyMatched) branch is unit-tested against fakes + relies on the F1
/// <see cref="FilterToMatchingTests"/> oracle. The shared <c>[Collection("Api")]</c> Postgres is never
/// reset, so every test uses fresh users + unique ids (memory: api_integration_shared_db_contamination).
/// </para>
/// </summary>
[Collection("Api")]
public class FollowedCompanyAdRailTests(ApiFactory factory)
{
    private const string NewCountEndpoint = "/api/v1/me/followed-company-ads/new-count";
    private const string SeenEndpoint = "/api/v1/me/followed-company-ads/seen";

    private sealed record CountResponse(int Count);

    private async Task<(HttpClient Client, Guid UserId)> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return (client, user.Id);
    }

    // Seeds a follow via the aggregate. active=false → unfollow it (SoftDelete). Returns the id.
    private async Task<CompanyWatchId> SeedWatchAsync(
        Guid userId, bool active, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(NewOrgNr()).Value, clock).Value;
        if (!active)
            watch.SoftDelete(clock);
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    // Seeds a hit at a SPECIFIC CreatedAt (via a fake clock into Create) with a given status.
    private async Task SeedHitAsync(
        Guid userId, CompanyWatchId watchId, DateTimeOffset createdAt,
        FollowedCompanyAdHitStatus status, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FakeDateTimeProvider(createdAt);
        var hit = FollowedCompanyAdHit.Create(userId, new JobAdId(Guid.NewGuid()), watchId, clock).Value;
        // Drive the state machine to the requested status (all statuses count in the rail — status-agnostic).
        switch (status)
        {
            case FollowedCompanyAdHitStatus.Queued:
                hit.MarkQueued();
                break;
            case FollowedCompanyAdHitStatus.Sent:
                hit.MarkQueued();
                hit.MarkSent(clock);
                break;
            case FollowedCompanyAdHitStatus.Failed:
                hit.MarkQueued();
                hit.MarkFailed();
                break;
        }
        db.FollowedCompanyAdHits.Add(hit);
        await db.SaveChangesAsync(ct);
    }

    private async Task SetWatermarkAsync(Guid userId, DateTimeOffset watermark, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.SetLastSeenFollowedAds(watermark, new FakeDateTimeProvider(watermark));
        await db.SaveChangesAsync(ct);
    }

    private static async Task<int> GetCountAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(NewCountEndpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CountResponse>(ct);
        body.ShouldNotBeNull();
        return body.Count;
    }

    [Fact]
    public async Task NewCount_CountsHitsAfterWatermark_StatusAgnostic()
    {
        // The equijoin on the value-converted CompanyWatchId MUST translate (this is the de-risk test).
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("rail-count", ct);
        var watchId = await SeedWatchAsync(userId, active: true, ct);
        var watermark = DateTimeOffset.UtcNow.AddHours(-1);
        await SetWatermarkAsync(userId, watermark, ct);

        // One BEFORE the watermark → excluded.
        await SeedHitAsync(userId, watchId, watermark.AddMinutes(-30), FollowedCompanyAdHitStatus.Pending, ct);
        // Four AFTER the watermark, one per status → ALL counted (status-agnostic, parity GetMyNewMatchCount).
        await SeedHitAsync(userId, watchId, watermark.AddMinutes(10), FollowedCompanyAdHitStatus.Pending, ct);
        await SeedHitAsync(userId, watchId, watermark.AddMinutes(20), FollowedCompanyAdHitStatus.Queued, ct);
        await SeedHitAsync(userId, watchId, watermark.AddMinutes(30), FollowedCompanyAdHitStatus.Sent, ct);
        await SeedHitAsync(userId, watchId, watermark.AddMinutes(40), FollowedCompanyAdHitStatus.Failed, ct);

        var count = await GetCountAsync(client, ct);

        count.ShouldBe(4, "only the four hits created after the watermark count — regardless of status");
    }

    [Fact]
    public async Task NewCount_ExcludesHitsFromUnfollowedWatch()
    {
        // An unfollowed (soft-deleted) watch drops out of the active-watch join → its hits never count
        // ("företag du bevakar" is present-tense; deliberate divergence from the dispatch).
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("rail-unfollow", ct);
        var activeWatch = await SeedWatchAsync(userId, active: true, ct);
        var unfollowedWatch = await SeedWatchAsync(userId, active: false, ct);
        var watermark = DateTimeOffset.UtcNow.AddHours(-1);
        await SetWatermarkAsync(userId, watermark, ct);

        await SeedHitAsync(userId, activeWatch, watermark.AddMinutes(10), FollowedCompanyAdHitStatus.Pending, ct);
        await SeedHitAsync(userId, unfollowedWatch, watermark.AddMinutes(20), FollowedCompanyAdHitStatus.Pending, ct);

        var count = await GetCountAsync(client, ct);

        count.ShouldBe(1, "the unfollowed watch's hit is excluded; only the active watch's hit counts");
    }

    [Fact]
    public async Task NewCount_NullWatermark_CountsAllActiveHits()
    {
        // Never visited (null watermark) → every hit is new.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("rail-null-wm", ct);
        var watchId = await SeedWatchAsync(userId, active: true, ct);

        await SeedHitAsync(userId, watchId, DateTimeOffset.UtcNow.AddDays(-5), FollowedCompanyAdHitStatus.Pending, ct);
        await SeedHitAsync(userId, watchId, DateTimeOffset.UtcNow.AddHours(-1), FollowedCompanyAdHitStatus.Sent, ct);

        var count = await GetCountAsync(client, ct);

        count.ShouldBe(2, "with no watermark every hit is new");
    }

    [Fact]
    public async Task NewCount_NoActiveFollows_IsZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterUserAsync("rail-empty", ct);

        var count = await GetCountAsync(client, ct);

        count.ShouldBe(0, "a user with no active follows has no rail count");
    }

    [Fact]
    public async Task NewCount_IsCrossUserIsolated()
    {
        // User B's hits must never count for user A (owner-scoped; no cross-user surface).
        var ct = TestContext.Current.CancellationToken;
        var (clientA, userIdA) = await RegisterUserAsync("rail-iso-a", ct);
        var (_, userIdB) = await RegisterUserAsync("rail-iso-b", ct);
        var watchB = await SeedWatchAsync(userIdB, active: true, ct);
        await SeedHitAsync(userIdB, watchB, DateTimeOffset.UtcNow, FollowedCompanyAdHitStatus.Pending, ct);
        _ = userIdA;

        var count = await GetCountAsync(clientA, ct);

        count.ShouldBe(0, "user A sees only their own follows' hits");
    }

    [Fact]
    public async Task NewCount_WithoutAuth_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.GetAsync(NewCountEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSeen_AdvancesWatermark_ResetsCount()
    {
        // Seed a hit (counted), advance the watermark via the endpoint (clock-now), then the same hit
        // (created before "now") no longer counts.
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("rail-seen", ct);
        var watchId = await SeedWatchAsync(userId, active: true, ct);
        await SeedHitAsync(userId, watchId, DateTimeOffset.UtcNow.AddMinutes(-5), FollowedCompanyAdHitStatus.Pending, ct);

        (await GetCountAsync(client, ct)).ShouldBe(1, "the hit is new before the visit");

        var seenResponse = await client.PostAsync(SeenEndpoint, content: null, ct);
        seenResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetCountAsync(client, ct)).ShouldBe(0, "visiting the hub advanced the watermark past the hit");
    }

    [Fact]
    public async Task PostSeen_WithoutAuth_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync(SeenEndpoint, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // 10-digit legal-entity org.nr (third digit ≥ 2), unique per call — OrganizationNumber.Create
    // validates 10 digits (no Luhn).
    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";
}
