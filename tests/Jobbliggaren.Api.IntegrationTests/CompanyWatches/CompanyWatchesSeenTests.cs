using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
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
/// #453 (cross-channel dedup; ADR 0087 D5-addendum) - end-to-end against Testcontainers Postgres for
/// <c>POST /api/v1/me/company-watches/ad-hits/{jobAdId}/seen</c>. Proves the auth gate (204 vs 401)
/// and the owner-scoped IDOR-safety the endpoint promises: marking user A's ad seen stamps ONLY user
/// A's hits (UserId from the session, never the wire), leaving user B's Pending+unseen hit for the SAME
/// ad untouched (it would still be dispatched). The shared <c>[Collection("Api")]</c> Postgres is never
/// reset, so every test uses a UNIQUE jobAdId + fresh users to stay contamination-free
/// (memory: api_integration_shared_db_contamination). FollowedCompanyAdHit has no FK on job_ad_id /
/// company_watch_id (ADR 0058/0059), so a hit can be seeded with fresh ids (no ad/watch rows needed).
/// </summary>
[Collection("Api")]
public class CompanyWatchesSeenTests(ApiFactory factory)
{
    private static string SeenEndpoint(Guid jobAdId) =>
        $"/api/v1/me/company-watches/ad-hits/{jobAdId}/seen";

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

    private async Task SeedPendingHitAsync(Guid userId, Guid jobAdId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var hit = FollowedCompanyAdHit.Create(userId, new JobAdId(jobAdId), CompanyWatchId.New(), clock).Value;
        db.FollowedCompanyAdHits.Add(hit);
        await db.SaveChangesAsync(ct);
    }

    private async Task<FollowedCompanyAdHit?> GetHitAsync(Guid userId, Guid jobAdId, CancellationToken ct)
    {
        var jobAdIdVo = new JobAdId(jobAdId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.UserId == userId && h.JobAdId == jobAdIdVo, ct);
    }

    [Fact]
    public async Task POST_ad_hits_seen_returns_204_for_authenticated_user()
    {
        // No hit seeded -> benign no-op, still 204 (an absent hit is indistinguishable from a present
        // one - never leaks follow-existence).
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await RegisterUserAsync("seen-204", ct);

        var response = await client.PostAsync(SeenEndpoint(Guid.NewGuid()), content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task POST_ad_hits_seen_stamps_authenticated_users_pending_hit()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("seen-stamp", ct);
        var jobAdId = Guid.NewGuid();
        await SeedPendingHitAsync(userId, jobAdId, ct);

        var response = await client.PostAsync(SeenEndpoint(jobAdId), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var hit = await GetHitAsync(userId, jobAdId, ct);
        hit!.SeenAt.ShouldNotBeNull("the authenticated user's Pending hit is stamped seen");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending, "MarkSeen only stamps");
    }

    [Fact]
    public async Task POST_ad_hits_seen_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync(SeenEndpoint(Guid.NewGuid()), content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_ad_hits_seen_marks_only_current_users_hit_not_another_users()
    {
        // Owner-scope / IDOR-safe: user A marking the ad seen must NOT touch user B's Pending hit for the
        // SAME ad - B's hit stays Pending+unseen and would still be dispatched.
        var ct = TestContext.Current.CancellationToken;
        var (clientA, userIdA) = await RegisterUserAsync("seen-iso-a", ct);
        var (_, userIdB) = await RegisterUserAsync("seen-iso-b", ct);
        var jobAdId = Guid.NewGuid();
        await SeedPendingHitAsync(userIdA, jobAdId, ct);
        await SeedPendingHitAsync(userIdB, jobAdId, ct);

        var response = await clientA.PostAsync(SeenEndpoint(jobAdId), content: null, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var hitA = await GetHitAsync(userIdA, jobAdId, ct);
        hitA!.SeenAt.ShouldNotBeNull("user A's own hit is stamped");

        var hitB = await GetHitAsync(userIdB, jobAdId, ct);
        hitB!.SeenAt.ShouldBeNull("user B's hit for the same ad is untouched (owner-scope)");
        hitB.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending,
            "user B's hit stays Pending+unseen and would still be dispatched");
    }
}
