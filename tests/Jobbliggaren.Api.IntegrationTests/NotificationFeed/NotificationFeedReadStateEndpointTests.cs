using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyMatches;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.NotificationFeed;

/// <summary>
/// #2130 PR 2 (ADR 0162 as amended) - the bell's per-row read state end to end against Testcontainers Postgres: the two
/// POST marks, the two PUT switches, and the read paths that now know a row can be read in the bell. Only a real provider
/// proves that the set-based writes (<c>ExecuteUpdate</c> with a correlated consent <c>EXISTS</c>), the grouped common-path
/// read and the flag predicates translate, and only the wired stack proves the surface: 401, the closed set of sources, the
/// 204 for every request from a signed-in owner, the buckets, the audit rows and what a mark leaves alone. Every seed goes
/// through an aggregate; every test uses fresh users (the shared <c>[Collection("Api")]</c> database is never reset).
/// </summary>
[Collection("Api")]
public class NotificationFeedReadStateEndpointTests(ApiFactory factory)
{
    private const string Feed = "/api/v1/me/notification-feed";
    private const string Matches = "matches";
    private const string FollowedAds = "followed-company-ads";

    private sealed record FeedItem(
        string Kind, Guid JobAdId, string Title, string CompanyName, DateTimeOffset OccurredAt, bool IsUnread, string? Grade);

    private sealed record FeedSource(string Kind, int? UnreadCount, DateTimeOffset? AcknowledgedThrough);

    private sealed record FeedBody(IReadOnlyList<FeedItem> Items, IReadOnlyList<FeedSource> Sources);

    private sealed record CountResponse(int Count);

    // Whole seconds in the past: the /seen setters clamp to now, and a seeded instant must survive timestamptz unchanged.
    private static readonly DateTimeOffset Base =
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds());

    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, Guid UserId)> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return (client, user.Id);
    }

    private async Task ConsentAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        await db.SaveChangesAsync(ct);
    }

    private async Task<JobAdId> SeedAdAsync(string title, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FakeDateTimeProvider(Base.AddHours(-1));
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Bolag AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: clock.UtcNow,
            expiresAt: clock.UtcNow.AddDays(60),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private async Task ArchiveAsync(JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == adId, ct);
        ad.Archive(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>()).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedMatchAsync(Guid userId, JobAdId adId, DateTimeOffset at, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserJobAdMatches.Add(
            UserJobAdMatch.Create(userId, adId, NotifiableMatchGrade.Strong, ["csharp"], new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    private async Task<CompanyWatchId> SeedWatchAsync(Guid userId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(
            userId, OrganizationNumber.Create(orgNr).Value, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>()).Value;
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    private async Task SeedHitAsync(Guid userId, JobAdId adId, CompanyWatchId watchId, DateTimeOffset at, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, adId, watchId, new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The user's click on a match, as the command ends it: the one set-based statement, without the consent predicate.</summary>
    private async Task ReadMatchInBellAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserJobAdMatches.Where(m => m.UserId == userId && m.JobAdId == adId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadInNotificationFeed, true), ct);
    }

    private async Task ReadHitsInBellAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var hit in await db.FollowedCompanyAdHits.Where(h => h.UserId == userId && h.JobAdId == adId).ToListAsync(ct))
            hit.MarkReadInNotificationFeed();
        await db.SaveChangesAsync(ct);
    }

    // ── reading back ─────────────────────────────────────────────────────────────────────────

    private async Task<UserJobAdMatch> MatchRowAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking().SingleAsync(m => m.UserId == userId && m.JobAdId == adId, ct);
    }

    private async Task<List<FollowedCompanyAdHit>> HitRowsAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits.AsNoTracking().Where(h => h.UserId == userId && h.JobAdId == adId).ToListAsync(ct);
    }

    private async Task<JobSeeker> SeekerAsync(Guid userId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JobSeekers.AsNoTracking().SingleAsync(js => js.UserId == userId, ct);
    }

    private async Task<int> AuditCountAsync(CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.CountAsync(ct);
    }

    private static async Task<FeedBody> GetFeedAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(Feed, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FeedBody>(ct))!;
    }

    private static FeedSource Source(FeedBody feed, string kind) => feed.Sources.Single(s => s.Kind == kind);

    private static async Task<int> CountAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CountResponse>(ct))!.Count;
    }

    private static Task<HttpResponseMessage> MarkRowAsync(HttpClient client, string source, Guid adId, CancellationToken ct) =>
        client.PostAsync($"{Feed}/{source}/{adId}/read", content: null, ct);

    private static Task<HttpResponseMessage> MarkThroughAsync(HttpClient client, string source, DateTimeOffset token, CancellationToken ct) =>
        client.PostAsJsonAsync($"{Feed}/{source}/read", new { throughToken = token.ToString("O") }, ct);

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient client, string source, bool enabled, CancellationToken ct) =>
        client.PutAsJsonAsync($"{Feed}/{source}", new { enabled }, ct);

    private static async Task<bool> ProfileFlagAsync(HttpClient client, string name, CancellationToken ct)
    {
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/profile", ct);
        return profile.GetProperty(name).GetBoolean();
    }

    // ── the surface ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_new_route_refuses_an_anonymous_caller_with_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var anonymous = factory.CreateClient();

        (await anonymous.PostAsync($"{Feed}/{Matches}/{Guid.NewGuid()}/read", null, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Feed}/{Matches}/read", new { throughToken = Base.ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"{Feed}/{Matches}", new { enabled = false }, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"{Feed}/{FollowedAds}", new { enabled = false }, ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "/api/v1/me/notification-feed/{source}/{jobAdId:guid}/read", RateLimitingExtensions.NotificationFeedMarkPolicy)]
    [InlineData("POST", "/api/v1/me/notification-feed/{source}/read", RateLimitingExtensions.NotificationFeedMarkPolicy)]
    [InlineData("PUT", "/api/v1/me/notification-feed/matches", RateLimitingExtensions.MeWritePolicy)]
    [InlineData("PUT", "/api/v1/me/notification-feed/followed-company-ads", RateLimitingExtensions.MeWritePolicy)]
    public void Each_route_requires_authorization_and_carries_its_own_policy(string method, string pattern, string policy)
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, pattern, StringComparison.Ordinal)
                        && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method))
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty();
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(policy);
    }

    // A 400 that the request alone decides, before any read, so it reveals nothing about anyone's rows: the same for a
    // user who has rows and one who has none. A source outside the closed set is never a bare enum name or number.
    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("Match")]
    [InlineData("FollowedCompanyAd")]
    [InlineData("application")]
    public async Task A_source_outside_the_closed_set_is_a_400_for_a_user_with_rows_and_one_without(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (withRows, withRowsId) = await RegisterUserAsync("mark-400-rows", ct);
        var (without, _) = await RegisterUserAsync("mark-400-none", ct);
        await ConsentAsync(withRowsId, true, ct);
        var ad = await SeedAdAsync("A", ct);
        await SeedMatchAsync(withRowsId, ad, Base.AddMinutes(1), ct);

        foreach (var client in new[] { withRows, without })
        {
            (await client.PostAsync($"{Feed}/{source}/{ad.Value}/read", null, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await client.PostAsJsonAsync($"{Feed}/{source}/read", new { throughToken = Base.ToString("O") }, ct))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task A_malformed_or_missing_token_and_the_empty_ad_id_are_400s_and_a_non_guid_id_matches_no_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-bad-input", ct);
        await ConsentAsync(userId, true, ct);

        (await client.PostAsJsonAsync($"{Feed}/{Matches}/read", new { throughToken = "yesterday" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"{Feed}/{Matches}/read", new { }, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"{Feed}/{Matches}/read", new { throughToken = "0001-01-01T00:00:00+00:00" }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MarkRowAsync(client, Matches, Guid.Empty, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync($"{Feed}/{Matches}/not-a-guid/read", null, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // The answer is 204 for every request from a signed-in owner, so it says nothing about whether the row exists,
    // whose it is, or whether the ad has ended (IDOR; security V6).
    [Fact]
    public async Task A_mark_answers_204_for_a_missing_a_foreign_and_an_ended_row_and_changes_no_other_users_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (me, myId) = await RegisterUserAsync("mark-204-me", ct);
        var (_, otherId) = await RegisterUserAsync("mark-204-other", ct);
        await ConsentAsync(myId, true, ct);
        await ConsentAsync(otherId, true, ct);
        var theirsMatch = await SeedAdAsync("Deras match", ct);
        var theirsFollow = await SeedAdAsync("Deras följda", ct);
        var endedAd = await SeedAdAsync("Avslutad", ct);
        await SeedMatchAsync(otherId, theirsMatch, Base.AddMinutes(1), ct);
        await SeedHitAsync(otherId, theirsFollow, await SeedWatchAsync(otherId, ct), Base.AddMinutes(1), ct);
        await SeedMatchAsync(myId, endedAd, Base.AddMinutes(2), ct);
        await ArchiveAsync(endedAd, ct);

        foreach (var (source, ad) in new[]
                 {
                     (Matches, Guid.NewGuid()),          // no such row
                     (Matches, theirsMatch.Value),       // someone else's
                     (FollowedAds, Guid.NewGuid()),
                     (FollowedAds, theirsFollow.Value),
                     (Matches, endedAd.Value),           // an ad that ended between the render and the click
                 })
        {
            (await MarkRowAsync(me, source, ad, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        (await MarkThroughAsync(me, FollowedAds, Base.AddDays(1), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(otherId, theirsMatch, ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await HitRowsAsync(otherId, theirsFollow, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
        // ADR 0168 D3: a mark reads no JobAds, so the owner's own row of an ended ad is flagged. The feed never lists it, so
        // nothing observes that, and the answer was the same 204.
        (await MatchRowAsync(myId, endedAd, ct)).ReadInNotificationFeed.ShouldBeTrue();
    }

    // #2138 is open: nothing is built that writes a match row while the consent is withdrawn. A mark is the same 204 and
    // no write, for the row, the set and the backlog alike. The backlog reaches only a row created AFTER the off-stamp, so
    // that row is seeded after it: a row from before the stamp stays unread whatever the consent says.
    [Fact]
    public async Task While_the_consent_is_withdrawn_a_mark_of_a_match_is_a_204_and_writes_no_match_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-no-consent", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync("Match", ct);
        await SeedMatchAsync(userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var arrivedWhileOff = await SeedAdAsync("Kom medan den var av", ct);
        await SeedMatchAsync(userId, arrivedWhileOff, DateTimeOffset.UtcNow.AddMinutes(10), ct);
        await ConsentAsync(userId, false, ct);

        (await MarkRowAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkRowAsync(client, Matches, arrivedWhileOff.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkThroughAsync(client, Matches, DateTimeOffset.UtcNow.AddDays(1), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(client, Matches, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(userId, ad, ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await MatchRowAsync(userId, arrivedWhileOff, ct)).ReadInNotificationFeed.ShouldBeFalse();
    }

    // The row mark answers to the consent only, never to the user's switch for the type: a click on a row the bell can no
    // longer show (the type was switched off in another tab) still marks it.
    [Fact]
    public async Task A_row_mark_of_a_match_still_writes_while_the_type_is_switched_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-type-off", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync("Match", ct);
        await SeedMatchAsync(userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MarkRowAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(userId, ad, ct)).ReadInNotificationFeed.ShouldBeTrue();
    }

    // ── what a mark is and is not ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Marking_a_match_reads_it_in_the_bell_and_leaves_the_email_state_the_watermark_and_Oversikt_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-match", ct);
        await ConsentAsync(userId, true, ct);
        var clicked = await SeedAdAsync("Klickad", ct);
        var untouched = await SeedAdAsync("Orörd", ct);
        await SeedMatchAsync(userId, clicked, Base.AddMinutes(1), ct);
        await SeedMatchAsync(userId, untouched, Base.AddMinutes(2), ct);
        var before = await MatchRowAsync(userId, clicked, ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenMatchesAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/new-match-count", ct);

        (await MarkRowAsync(client, Matches, clicked.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await MatchRowAsync(userId, clicked, ct);
        after.ReadInNotificationFeed.ShouldBeTrue();
        after.NotificationStatus.ShouldBe(before.NotificationStatus);
        after.SentAt.ShouldBe(before.SentAt);
        after.CreatedAt.ShouldBe(before.CreatedAt);
        (await MatchRowAsync(userId, untouched, ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await SeekerAsync(userId, ct)).LastSeenMatchesAt.ShouldBe(watermark);
        // Översikt's count is the watermark's: a bell mark never lowers it (Klas 2026-10-11).
        (await CountAsync(client, "/api/v1/me/new-match-count", ct)).ShouldBe(oversiktBefore);
        var feed = await GetFeedAsync(client, ct);
        Source(feed, "Match").UnreadCount.ShouldBe(1);
        feed.Items.Single(i => i.JobAdId == clicked.Value).IsUnread.ShouldBeFalse();
        feed.Items.Single(i => i.JobAdId == untouched.Value).IsUnread.ShouldBeTrue();
    }

    [Fact]
    public async Task Marking_a_followed_ad_reads_every_hit_of_the_ad_and_leaves_SeenAt_the_watermark_and_Oversikt_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-follow", ct);
        var ad = await SeedAdAsync("Följd", ct);
        var elsewhere = await SeedAdAsync("Annan", ct);
        var watchA = await SeedWatchAsync(userId, ct);
        var watchB = await SeedWatchAsync(userId, ct);
        await SeedHitAsync(userId, ad, watchA, Base.AddMinutes(1), ct);
        await SeedHitAsync(userId, ad, watchB, Base.AddMinutes(2), ct);
        await SeedHitAsync(userId, elsewhere, watchA, Base.AddMinutes(3), ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct);

        (await MarkRowAsync(client, FollowedAds, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var hits = await HitRowsAsync(userId, ad, ct);
        hits.Count.ShouldBe(2);
        hits.ShouldAllBe(h => h.ReadInNotificationFeed);
        hits.ShouldAllBe(h => h.SeenAt == null, "reading in the bell is not seeing the ad for the e-mail dedup (F3)");
        hits.ShouldAllBe(h => h.NotificationStatus == FollowedCompanyAdHitStatus.Pending);
        (await HitRowsAsync(userId, elsewhere, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
        (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt.ShouldBe(watermark);
        (await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct)).ShouldBe(oversiktBefore);
        Source(await GetFeedAsync(client, ct), "FollowedCompanyAd").UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_token_still_covers_a_row_read_in_the_bell()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-token", ct);
        await ConsentAsync(userId, true, ct);
        var older = await SeedAdAsync("Äldre", ct);
        var newest = await SeedAdAsync("Nyast", ct);
        await SeedMatchAsync(userId, older, Base.AddMinutes(1), ct);
        await SeedMatchAsync(userId, newest, Base.AddMinutes(2), ct);
        var watch = await SeedWatchAsync(userId, ct);
        var olderFollowed = await SeedAdAsync("Äldre följd", ct);
        var newestFollowed = await SeedAdAsync("Nyast följd", ct);
        await SeedHitAsync(userId, olderFollowed, watch, Base.AddMinutes(3), ct);
        await SeedHitAsync(userId, newestFollowed, watch, Base.AddMinutes(4), ct);
        await ReadMatchInBellAsync(userId, newest, ct);
        await ReadHitsInBellAsync(userId, newestFollowed, ct);

        var feed = await GetFeedAsync(client, ct);

        // A token computed over unread rows only would sit below the read rows, and "Markera alla" would strand them.
        Source(feed, "Match").AcknowledgedThrough.ShouldBe(Base.AddMinutes(2));
        Source(feed, "FollowedCompanyAd").AcknowledgedThrough.ShouldBe(Base.AddMinutes(4));
        Source(feed, "Match").UnreadCount.ShouldBe(1);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task One_user_marking_never_changes_another_users_feed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (mine, myId) = await RegisterUserAsync("mark-iso-me", ct);
        var (theirs, theirId) = await RegisterUserAsync("mark-iso-other", ct);
        await ConsentAsync(myId, true, ct);
        await ConsentAsync(theirId, true, ct);
        var shared = await SeedAdAsync("Delad", ct);
        await SeedMatchAsync(myId, shared, Base.AddMinutes(1), ct);
        await SeedMatchAsync(theirId, shared, Base.AddMinutes(1), ct);

        (await MarkRowAsync(mine, Matches, shared.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Source(await GetFeedAsync(mine, ct), "Match").UnreadCount.ShouldBe(0);
        Source(await GetFeedAsync(theirs, ct), "Match").UnreadCount.ShouldBe(1);
    }

    // ── "Markera alla som lästa" ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task Mark_all_reads_the_counted_set_up_to_the_token_and_leaves_a_row_after_it_unread(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"markall-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var watch = source == FollowedAds ? await SeedWatchAsync(userId, ct) : default;
        var ads = new List<JobAdId>();
        for (var i = 0; i < 3; i++)
        {
            var ad = await SeedAdAsync($"Annons {i}", ct);
            ads.Add(ad);
            if (source == Matches)
                await SeedMatchAsync(userId, ad, Base.AddMinutes(1 + i), ct);
            else
                await SeedHitAsync(userId, ad, watch, Base.AddMinutes(1 + i), ct);
        }
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        var token = Source(await GetFeedAsync(client, ct), kind).AcknowledgedThrough!.Value;
        // A row that arrives after the number was read is neither counted nor marked: it stays unread for the next read.
        var late = await SeedAdAsync("Sen annons", ct);
        if (source == Matches)
            await SeedMatchAsync(userId, late, token.AddMinutes(1), ct);
        else
            await SeedHitAsync(userId, late, watch, token.AddMinutes(1), ct);
        var watermarks = await SeekerAsync(userId, ct);
        var oversikt = source == Matches
            ? await CountAsync(client, "/api/v1/me/new-match-count", ct)
            : await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct);

        (await MarkThroughAsync(client, source, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkThroughAsync(client, source, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent); // idempotent

        var feed = await GetFeedAsync(client, ct);
        Source(feed, kind).UnreadCount.ShouldBe(1, "only the row that arrived after the token");
        feed.Items.Where(i => i.Kind == kind && ads.Any(a => a.Value == i.JobAdId)).ShouldAllBe(i => !i.IsUnread);
        feed.Items.Single(i => i.JobAdId == late.Value).IsUnread.ShouldBeTrue();
        // No watermark moved, so Översikt's number is exactly what it was.
        var after = await SeekerAsync(userId, ct);
        after.LastSeenMatchesAt.ShouldBe(watermarks.LastSeenMatchesAt);
        after.LastSeenFollowedAdsAt.ShouldBe(watermarks.LastSeenFollowedAdsAt);
        (source == Matches
            ? await CountAsync(client, "/api/v1/me/new-match-count", ct)
            : await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct)).ShouldBe(oversikt);
    }

    [Fact]
    public async Task Mark_all_writes_only_the_bell_flag_and_never_a_row_at_or_before_the_watermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("markall-flag-only", ct);
        await ConsentAsync(userId, true, ct);
        var acknowledged = await SeedAdAsync("Redan kvitterad", ct);
        var fresh = await SeedAdAsync("Ny", ct);
        await SeedMatchAsync(userId, acknowledged, Base.AddMinutes(1), ct);
        await SeedMatchAsync(userId, fresh, Base.AddMinutes(3), ct);
        // The user visited /matchningar: the watermark now sits past the first row.
        (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = Base.AddMinutes(2).ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var before = await MatchRowAsync(userId, fresh, ct);

        (await MarkThroughAsync(client, Matches, Base.AddMinutes(5), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var marked = await MatchRowAsync(userId, fresh, ct);
        marked.ReadInNotificationFeed.ShouldBeTrue();
        marked.NotificationStatus.ShouldBe(before.NotificationStatus);
        marked.SentAt.ShouldBe(before.SentAt);
        (await MatchRowAsync(userId, acknowledged, ct)).ReadInNotificationFeed.ShouldBeFalse(
            "a row the watermark already acknowledged is not counted, so it is not written");
        (await SeekerAsync(userId, ct)).LastSeenMatchesAt.ShouldBe(Base.AddMinutes(2));
    }

    // ── the switches ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Matches, "notificationFeedMatchesEnabled")]
    [InlineData(FollowedAds, "notificationFeedFollowedAdsEnabled")]
    public async Task A_switch_returns_204_the_profile_reads_it_back_and_the_feed_honours_it(string source, string profileFlag)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"switch-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync("Rad", ct);
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        if (source == Matches)
            await SeedMatchAsync(userId, ad, Base.AddMinutes(1), ct);
        else
            await SeedHitAsync(userId, ad, await SeedWatchAsync(userId, ct), Base.AddMinutes(1), ct);
        (await ProfileFlagAsync(client, profileFlag, ct)).ShouldBeTrue();
        Source(await GetFeedAsync(client, ct), kind).UnreadCount.ShouldBe(1);

        (await SwitchAsync(client, source, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ProfileFlagAsync(client, profileFlag, ct)).ShouldBeFalse();
        var off = await GetFeedAsync(client, ct);
        // The source is not read at all: no rows, no count, no token to acknowledge.
        Source(off, kind).ShouldBe(new FeedSource(kind, 0, null));
        off.Items.Where(i => i.Kind == kind).ShouldBeEmpty();

        (await SwitchAsync(client, source, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ProfileFlagAsync(client, profileFlag, ct)).ShouldBeTrue();
        // What was unread before the switch-off is unread again: a switch only decides what reaches the bell.
        Source(await GetFeedAsync(client, ct), kind).UnreadCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task Switching_a_type_back_on_shows_nothing_from_the_time_it_was_off_as_unread_and_keeps_what_was_unread_before(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"backlog-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        var watch = source == FollowedAds ? await SeedWatchAsync(userId, ct) : default;
        var before = await SeedAdAsync("Före avstängningen", ct);
        var during = await SeedAdAsync("Medan den var av", ct);

        async Task SeedAsync(JobAdId ad, DateTimeOffset at)
        {
            if (source == Matches)
                await SeedMatchAsync(userId, ad, at, ct);
            else
                await SeedHitAsync(userId, ad, watch, at, ct);
        }

        await SeedAsync(before, Base.AddMinutes(1));
        (await SwitchAsync(client, source, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Rows are created while the type is off: after the off-stamp, which is "now".
        await SeedAsync(during, DateTimeOffset.UtcNow.AddMinutes(10));
        (await GetFeedAsync(client, ct)).Items.Where(i => i.Kind == kind).ShouldBeEmpty();

        (await SwitchAsync(client, source, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var feed = await GetFeedAsync(client, ct);
        feed.Items.Single(i => i.Kind == kind && i.JobAdId == before.Value).IsUnread.ShouldBeTrue();
        // Marked read at switch-on, so it arrives as read history, not as a flood of unread (and the badge says 1, not 2).
        feed.Items.Single(i => i.Kind == kind && i.JobAdId == during.Value).IsUnread.ShouldBeFalse();
        Source(feed, kind).UnreadCount.ShouldBe(1);
        if (source == Matches)
            (await MatchRowAsync(userId, during, ct)).ReadInNotificationFeed.ShouldBeTrue();
        else
            (await HitRowsAsync(userId, during, ct)).ShouldAllBe(h => h.ReadInNotificationFeed);
    }

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task Switching_on_when_the_type_is_already_on_marks_nothing(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"backlog-noop-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync("Ny", ct);
        // Created "in the future" relative to anything a null stamp could mean: with no stamp the backlog is empty, and
        // a null stamp must never read as "since forever".
        if (source == Matches)
            await SeedMatchAsync(userId, ad, DateTimeOffset.UtcNow.AddMinutes(10), ct);
        else
            await SeedHitAsync(userId, ad, await SeedWatchAsync(userId, ct), DateTimeOffset.UtcNow.AddMinutes(10), ct);

        (await SwitchAsync(client, source, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        if (source == Matches)
            (await MatchRowAsync(userId, ad, ct)).ReadInNotificationFeed.ShouldBeFalse();
        else
            (await HitRowsAsync(userId, ad, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
    }

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task A_switch_without_enabled_is_a_400_that_writes_nothing(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"switch-400-{source}", ct);
        var auditBefore = await AuditCountAsync(ct);

        var response = await client.PutAsJsonAsync($"{Feed}/{source}", new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var seeker = await SeekerAsync(userId, ct);
        seeker.Preferences.NotificationFeedMatchesOffAt.ShouldBeNull();
        seeker.Preferences.NotificationFeedFollowedAdsOffAt.ShouldBeNull();
        (await AuditCountAsync(ct)).ShouldBe(auditBefore);
    }

    [Theory]
    [InlineData(Matches, "JobSeeker.NotificationFeedMatchesUpdated")]
    [InlineData(FollowedAds, "JobSeeker.NotificationFeedFollowedAdsUpdated")]
    public async Task A_switch_writes_exactly_one_audit_row_carrying_only_the_requested_enabled(string source, string eventType)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync($"switch-audit-{source}", ct);
        var jobSeekerId = (await SeekerAsync(userId, ct)).Id.Value;

        (await SwitchAsync(client, source, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = (await db.AuditLogEntries.Where(a => a.AggregateId == jobSeekerId).ToListAsync(ct))
            .Where(a => a.EventType == eventType).ShouldHaveSingleItem();
        using var payload = JsonDocument.Parse(entry.Payload!);
        payload.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["enabled"]);
        payload.RootElement.GetProperty("enabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Marks_write_no_audit_row_and_the_profile_carries_no_off_stamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("mark-no-audit", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync("Rad", ct);
        await SeedMatchAsync(userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var before = await AuditCountAsync(ct);

        (await MarkRowAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkThroughAsync(client, Matches, Base.AddDays(1), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AuditCountAsync(ct)).ShouldBe(before, "a read mark is high-frequency behavioural data and is not audited");
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/profile", ct);
        profile.EnumerateObject().Select(p => p.Name).ShouldNotContain(name => name.EndsWith("OffAt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_switch_writes_only_its_own_stamp_never_a_consent_a_mail_pause_or_the_other_switch()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("switch-own-stamp", ct);
        await ConsentAsync(userId, true, ct);
        var before = (await SeekerAsync(userId, ct)).Preferences;

        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(client, FollowedAds, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(client, Matches, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = (await SeekerAsync(userId, ct)).Preferences;
        after.ShouldBe(before with { NotificationFeedFollowedAdsOffAt = after.NotificationFeedFollowedAdsOffAt });
        after.NotificationFeedMatchesOffAt.ShouldBeNull();
        after.NotificationFeedFollowedAdsOffAt.ShouldNotBeNull();
    }

    // ── the read paths on Npgsql ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_older_unread_match_behind_twenty_newer_rows_read_in_the_bell_is_listed_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("crowd-match", ct);
        await ConsentAsync(userId, true, ct);
        var unread = await SeedAdAsync("Oläst", ct);
        await SeedMatchAsync(userId, unread, Base.AddMinutes(1), ct);
        for (var i = 0; i < 21; i++)
        {
            var read = await SeedAdAsync($"Läst {i}", ct);
            await SeedMatchAsync(userId, read, Base.AddMinutes(10 + i), ct);
            await ReadMatchInBellAsync(userId, read, ct);
        }

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "Match").UnreadCount.ShouldBe(1);
        feed.Items.Count(i => i.IsUnread).ShouldBe(1);
        feed.Items.Single(i => i.IsUnread).JobAdId.ShouldBe(unread.Value);
        feed.Items.Select(i => (i.Kind, i.JobAdId)).Distinct().Count().ShouldBe(feed.Items.Count);
    }

    // The common path under-fill (#2130 PR 1's named skip): no "endast matchade" watch, so every hit is included and the
    // grade-path reserve never ran. The window reads three hits per row the panel can show, so the source is driven with a
    // small cap: nine hits fill the window with ads the user has read, and the older unread ad can be named only by the
    // grouped statement, which joins JobAds and orders by the grouped maximum.
    [Fact]
    public async Task On_the_common_path_the_grouped_statement_names_an_unread_ad_the_window_left_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync("crowd-follow", ct);
        var watch = await SeedWatchAsync(userId, ct);
        var unread = await SeedAdAsync("Oläst följd", ct);
        await SeedHitAsync(userId, unread, watch, Base.AddMinutes(1), ct);
        var newestUnread = await SeedAdAsync("Nyaste olästa följd", ct);
        await SeedHitAsync(userId, newestUnread, watch, Base.AddMinutes(2), ct);
        for (var i = 0; i < 10; i++)
        {
            var read = await SeedAdAsync($"Läst följd {i}", ct);
            await SeedHitAsync(userId, read, watch, Base.AddMinutes(10 + i), ct);
            await ReadHitsInBellAsync(userId, read, ct);
        }

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var read2 = await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(
                db, userId, MaxItems: 3, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>()),
            ct);

        read2.UnreadCount.ShouldBe(2);
        read2.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([newestUnread.Value, unread.Value]);
        read2.Items.Select(i => i.JobAdId).Distinct().Count().ShouldBe(read2.Items.Count);
        read2.Items.Single(i => i.JobAdId == unread.Value).Title.ShouldBe("Oläst följd");
    }

    // Eleven, not nine, when read rows hide unread ones in BOTH sources: the two conditional statements run. The base case
    // (nothing read in the bell) stays at nine, pinned by NotificationFeedEndpointTests. Driven with a small cap, which
    // shrinks the window with it, so a handful of rows is enough.
    [Fact]
    public async Task A_request_whose_read_rows_hide_unread_ones_in_both_sources_runs_eleven_statements()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync("crowd-cost", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(userId, ct);
        await SeedMatchAsync(userId, await SeedAdAsync("Oläst match", ct), Base.AddMinutes(1), ct);
        await SeedHitAsync(userId, await SeedAdAsync("Oläst följd", ct), watch, Base.AddMinutes(1), ct);
        for (var i = 0; i < 8; i++)
        {
            var match = await SeedAdAsync($"Läst match {i}", ct);
            await SeedMatchAsync(userId, match, Base.AddMinutes(10 + i), ct);
            await ReadMatchInBellAsync(userId, match, ct);
            var followed = await SeedAdAsync($"Läst följd {i}", ct);
            await SeedHitAsync(userId, followed, watch, Base.AddMinutes(10 + i), ct);
            await ReadHitsInBellAsync(userId, followed, ct);
        }

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var context = new NotificationFeedReadContext(
            db, userId, MaxItems: 2, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

        var matches = await new MatchFeedSource().ReadAsync(context, ct);
        var followed2 = await new FollowedCompanyAdFeedSource().ReadAsync(context, ct);

        matches.Items.Count(i => i.IsUnread).ShouldBe(1);
        followed2.Items.Count(i => i.IsUnread).ShouldBe(1);
        // Match: consent + watermark, token, count, list, unread beyond the list. Followed: watermark, watches, token, count,
        // window, unread beyond the window.
        counter.Count.ShouldBe(11);
    }

    // I6: per source the bell's unread is at most Översikt's, and exactly Översikt's minus the rows read one by one.
    [Fact]
    public async Task The_bells_count_is_Oversikts_count_minus_exactly_the_rows_read_in_the_bell()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("parity-flags", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(userId, ct);
        var matchAds = new List<JobAdId>();
        var followAds = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var m = await SeedAdAsync($"M{i}", ct);
            var f = await SeedAdAsync($"F{i}", ct);
            matchAds.Add(m);
            followAds.Add(f);
            await SeedMatchAsync(userId, m, Base.AddMinutes(1 + i), ct);
            await SeedHitAsync(userId, f, watch, Base.AddMinutes(1 + i), ct);
        }

        var feed = await GetFeedAsync(client, ct);
        Source(feed, "Match").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/new-match-count", ct));
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct));

        await ReadMatchInBellAsync(userId, matchAds[0], ct);
        await ReadMatchInBellAsync(userId, matchAds[2], ct);
        await ReadHitsInBellAsync(userId, followAds[1], ct);

        feed = await GetFeedAsync(client, ct);
        Source(feed, "Match").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/new-match-count", ct) - 2);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct) - 1);
    }

    // The bell's flag is the bell's own: neither Översikt's counts nor the list pages ever read it, so reading or switching
    // in the bell cannot change what they show. Asserted on the SQL itself.
    [Fact]
    public async Task Oversikt_and_the_lists_never_read_the_bells_flag()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync("sql-flag", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(userId, ct);
        var onlyMatched = await SeedWatchOnlyMatchedAsync(userId, ct);
        var matchAd = await SeedAdAsync("M", ct);
        var followAd = await SeedAdAsync("F", ct);
        await SeedMatchAsync(userId, matchAd, Base.AddMinutes(1), ct);
        await SeedHitAsync(userId, followAd, watch, Base.AddMinutes(1), ct);
        await SeedHitAsync(userId, await SeedAdAsync("G", ct), onlyMatched, Base.AddMinutes(2), ct);

        using var scope = factory.Services.CreateScope();
        var statements = new StatementText();
        await using var db = NewContext(scope, statements);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));
        var search = Substitute.For<IPerUserJobAdSearchQuery>();
        search.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>());

        await new GetMyNewMatchCountQueryHandler(db, currentUser).Handle(new GetMyNewMatchCountQuery(), ct);
        await new GetMyMatchesQueryHandler(db, currentUser).Handle(new GetMyMatchesQuery(), ct);
        await new GetNewFollowedCompanyAdCountQueryHandler(db, currentUser, profileBuilder, search)
            .Handle(new GetNewFollowedCompanyAdCountQuery(), ct);
        await new ListNewFollowedCompanyAdsQueryHandler(db, currentUser, profileBuilder, search)
            .Handle(new ListNewFollowedCompanyAdsQuery(), ct);

        statements.Texts.ShouldNotBeEmpty();
        statements.Texts.ShouldNotContain(text => text.Contains("read_in_notification_feed", StringComparison.OrdinalIgnoreCase));
    }

    // The grade path over Npgsql with EXCLUDED hits (#2130 PR 1's named skip, closed here): an "endast matchade" watch's
    // newest hits grade below Good and fill the window, so the older ads that do match are named only by the fallback, which
    // runs the counted-hits-to-ads read the in-memory tests cannot translate. The grader is the one stub, as a profile
    // build and a search belong to other contexts; everything else is the real provider.
    [Fact]
    public async Task On_the_grade_path_hits_the_grader_excludes_fill_the_window_and_the_older_matching_unread_ads_are_still_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync("grade-exclusion", ct);
        var watch = await SeedWatchOnlyMatchedAsync(userId, ct);
        var matching = new List<JobAdId>();
        for (var i = 1; i <= 3; i++)
        {
            var ad = await SeedAdAsync($"Matchande {i}", ct);
            matching.Add(ad);
            await SeedHitAsync(userId, ad, watch, Base.AddMinutes(i), ct);
        }

        const int maxItems = 3;
        var window = maxItems * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 1; i++)
            await SeedHitAsync(userId, await SeedAdAsync($"Utesluten {i}", ct), watch, Base.AddHours(1).AddMinutes(i), ct);

        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));
        var search = Substitute.For<IPerUserJobAdSearchQuery>();
        search.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>(matching));

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var read = await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, maxItems, profileBuilder, search), ct);

        read.UnreadCount.ShouldBe(3);
        read.Items.ShouldAllBe(i => i.IsUnread);
        read.Items.Select(i => i.Title).ShouldBe(["Matchande 3", "Matchande 2", "Matchande 1"]);
        read.AcknowledgedThrough.ShouldBe(Base.AddHours(1).AddMinutes(window + 1));
        await search.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>());
    }

    private async Task<CompanyWatchId> SeedWatchOnlyMatchedAsync(Guid userId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(
            userId, OrganizationNumber.Create(orgNr).Value, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>()).Value;
        watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    /// <summary>
    /// A fresh <see cref="AppDbContext"/> on the fixture's database with only the given interceptors (the idiom of
    /// <c>NotificationFeedEndpointTests</c>). None of the tables read here carries a DEK-encrypted column.
    /// </summary>
    private static AppDbContext NewContext(IServiceScope scope, params IInterceptor[] interceptors)
    {
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors.Length > 0)
            builder = builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    /// <summary>Counts the statements a context sends. EF runs every query here through a reader.</summary>
    private sealed class StatementCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Keeps the text of every statement a context sends.</summary>
    private sealed class StatementText : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
