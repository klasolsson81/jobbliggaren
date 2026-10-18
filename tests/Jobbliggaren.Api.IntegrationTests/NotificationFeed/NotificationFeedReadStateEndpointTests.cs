using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.NotificationFeed.NotificationFeedTestSeed;

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

    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    private async Task ConsentAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
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

    // ── reading back ─────────────────────────────────────────────────────────────────────────

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
        var (withRows, withRowsId) = await RegisterUserAsync(factory, "mark-400-rows", ct);
        var (without, _) = await RegisterUserAsync(factory, "mark-400-none", ct);
        await ConsentAsync(withRowsId, true, ct);
        var ad = await SeedAdAsync(factory, "A", ct);
        await SeedMatchAsync(factory, withRowsId, ad, Base.AddMinutes(1), ct);

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
        var (client, userId) = await RegisterUserAsync(factory, "mark-bad-input", ct);
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
        var (me, myId) = await RegisterUserAsync(factory, "mark-204-me", ct);
        var (_, otherId) = await RegisterUserAsync(factory, "mark-204-other", ct);
        await ConsentAsync(myId, true, ct);
        await ConsentAsync(otherId, true, ct);
        var theirsMatch = await SeedAdAsync(factory, "Deras match", ct);
        var theirsFollow = await SeedAdAsync(factory, "Deras följda", ct);
        var endedAd = await SeedAdAsync(factory, "Avslutad", ct);
        await SeedMatchAsync(factory, otherId, theirsMatch, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, otherId, theirsFollow, await SeedWatchAsync(factory, otherId, ct), Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, myId, endedAd, Base.AddMinutes(2), ct);
        await ArchiveAsync(factory, endedAd, ct);

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

        (await MatchRowAsync(factory, otherId, theirsMatch, ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await HitRowsAsync(factory, otherId, theirsFollow, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
        // ADR 0168 D3: a mark reads no JobAds, so the owner's own row of an ended ad is flagged. The feed never lists it, so
        // nothing observes that, and the answer was the same 204.
        (await MatchRowAsync(factory, myId, endedAd, ct)).ReadInNotificationFeed.ShouldBeTrue();
    }

    // #2138 is open: nothing is built that writes a match row while the consent is withdrawn. A mark is the same 204 and
    // no write, for the row, the set and the backlog alike. The backlog reaches only a row created AFTER the off-stamp, so
    // that row is seeded after it: a row from before the stamp stays unread whatever the consent says.
    [Fact]
    public async Task While_the_consent_is_withdrawn_a_mark_of_a_match_is_a_204_and_writes_no_match_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "mark-no-consent", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Match", ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var arrivedWhileOff = await SeedAdAsync(factory, "Kom medan den var av", ct);
        await SeedMatchAsync(factory, userId, arrivedWhileOff, DateTimeOffset.UtcNow.AddMinutes(10), ct);
        await ConsentAsync(userId, false, ct);

        (await MarkRowAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkRowAsync(client, Matches, arrivedWhileOff.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkThroughAsync(client, Matches, DateTimeOffset.UtcNow.AddDays(1), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(client, Matches, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(factory, userId, ad, ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await MatchRowAsync(factory, userId, arrivedWhileOff, ct)).ReadInNotificationFeed.ShouldBeFalse();
    }

    // The row mark answers to the consent only, never to the user's switch for the type: a click on a row the bell can no
    // longer show (the type was switched off in another tab) still marks it.
    [Fact]
    public async Task A_row_mark_of_a_match_still_writes_while_the_type_is_switched_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "mark-type-off", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Match", ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MarkRowAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(factory, userId, ad, ct)).ReadInNotificationFeed.ShouldBeTrue();
    }

    // ── what a mark is and is not ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Marking_a_match_reads_it_in_the_bell_and_leaves_the_email_state_the_watermark_and_Oversikt_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "mark-match", ct);
        await ConsentAsync(userId, true, ct);
        var clicked = await SeedAdAsync(factory, "Klickad", ct);
        var untouched = await SeedAdAsync(factory, "Orörd", ct);
        await SeedMatchAsync(factory, userId, clicked, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, untouched, Base.AddMinutes(2), ct);
        var before = await MatchRowAsync(factory, userId, clicked, ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenMatchesAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/new-match-count", ct);

        (await MarkRowAsync(client, Matches, clicked.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await MatchRowAsync(factory, userId, clicked, ct);
        after.ReadInNotificationFeed.ShouldBeTrue();
        after.NotificationStatus.ShouldBe(before.NotificationStatus);
        after.SentAt.ShouldBe(before.SentAt);
        after.CreatedAt.ShouldBe(before.CreatedAt);
        (await MatchRowAsync(factory, userId, untouched, ct)).ReadInNotificationFeed.ShouldBeFalse();
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
        var (client, userId) = await RegisterUserAsync(factory, "mark-follow", ct);
        var ad = await SeedAdAsync(factory, "Följd", ct);
        var elsewhere = await SeedAdAsync(factory, "Annan", ct);
        var watchA = await SeedWatchAsync(factory, userId, ct);
        var watchB = await SeedWatchAsync(factory, userId, ct);
        await SeedHitAsync(factory, userId, ad, watchA, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, ad, watchB, Base.AddMinutes(2), ct);
        await SeedHitAsync(factory, userId, elsewhere, watchA, Base.AddMinutes(3), ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct);

        (await MarkRowAsync(client, FollowedAds, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var hits = await HitRowsAsync(factory, userId, ad, ct);
        hits.Count.ShouldBe(2);
        hits.ShouldAllBe(h => h.ReadInNotificationFeed);
        hits.ShouldAllBe(h => h.SeenAt == null, "reading in the bell is not seeing the ad for the e-mail dedup (F3)");
        hits.ShouldAllBe(h => h.NotificationStatus == FollowedCompanyAdHitStatus.Pending);
        (await HitRowsAsync(factory, userId, elsewhere, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
        (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt.ShouldBe(watermark);
        (await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct)).ShouldBe(oversiktBefore);
        Source(await GetFeedAsync(client, ct), "FollowedCompanyAd").UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_token_still_covers_a_row_read_in_the_bell()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "mark-token", ct);
        await ConsentAsync(userId, true, ct);
        var older = await SeedAdAsync(factory, "Äldre", ct);
        var newest = await SeedAdAsync(factory, "Nyast", ct);
        await SeedMatchAsync(factory, userId, older, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, newest, Base.AddMinutes(2), ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var olderFollowed = await SeedAdAsync(factory, "Äldre följd", ct);
        var newestFollowed = await SeedAdAsync(factory, "Nyast följd", ct);
        await SeedHitAsync(factory, userId, olderFollowed, watch, Base.AddMinutes(3), ct);
        await SeedHitAsync(factory, userId, newestFollowed, watch, Base.AddMinutes(4), ct);
        await ReadMatchInBellAsync(userId, newest, ct);
        await ReadHitsInBellAsync(factory, userId, newestFollowed, ct);

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
        var (mine, myId) = await RegisterUserAsync(factory, "mark-iso-me", ct);
        var (theirs, theirId) = await RegisterUserAsync(factory, "mark-iso-other", ct);
        await ConsentAsync(myId, true, ct);
        await ConsentAsync(theirId, true, ct);
        var shared = await SeedAdAsync(factory, "Delad", ct);
        await SeedMatchAsync(factory, myId, shared, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, theirId, shared, Base.AddMinutes(1), ct);

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
        var (client, userId) = await RegisterUserAsync(factory, $"markall-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var watch = source == FollowedAds ? await SeedWatchAsync(factory, userId, ct) : default;
        var ads = new List<JobAdId>();
        for (var i = 0; i < 3; i++)
        {
            var ad = await SeedAdAsync(factory, $"Annons {i}", ct);
            ads.Add(ad);
            if (source == Matches)
                await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1 + i), ct);
            else
                await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(1 + i), ct);
        }
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        var token = Source(await GetFeedAsync(client, ct), kind).AcknowledgedThrough!.Value;
        // A row that arrives after the number was read is neither counted nor marked: it stays unread for the next read.
        var late = await SeedAdAsync(factory, "Sen annons", ct);
        if (source == Matches)
            await SeedMatchAsync(factory, userId, late, token.AddMinutes(1), ct);
        else
            await SeedHitAsync(factory, userId, late, watch, token.AddMinutes(1), ct);
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
        var (client, userId) = await RegisterUserAsync(factory, "markall-flag-only", ct);
        await ConsentAsync(userId, true, ct);
        var acknowledged = await SeedAdAsync(factory, "Redan kvitterad", ct);
        var fresh = await SeedAdAsync(factory, "Ny", ct);
        await SeedMatchAsync(factory, userId, acknowledged, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, fresh, Base.AddMinutes(3), ct);
        // The user visited /matchningar: the watermark now sits past the first row.
        (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = Base.AddMinutes(2).ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var before = await MatchRowAsync(factory, userId, fresh, ct);

        (await MarkThroughAsync(client, Matches, Base.AddMinutes(5), ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var marked = await MatchRowAsync(factory, userId, fresh, ct);
        marked.ReadInNotificationFeed.ShouldBeTrue();
        marked.NotificationStatus.ShouldBe(before.NotificationStatus);
        marked.SentAt.ShouldBe(before.SentAt);
        (await MatchRowAsync(factory, userId, acknowledged, ct)).ReadInNotificationFeed.ShouldBeFalse(
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
        var (client, userId) = await RegisterUserAsync(factory, $"switch-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Rad", ct);
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        if (source == Matches)
            await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        else
            await SeedHitAsync(factory, userId, ad, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);
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
        var (client, userId) = await RegisterUserAsync(factory, $"backlog-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        var watch = source == FollowedAds ? await SeedWatchAsync(factory, userId, ct) : default;
        var before = await SeedAdAsync(factory, "Före avstängningen", ct);
        var during = await SeedAdAsync(factory, "Medan den var av", ct);

        async Task SeedAsync(JobAdId ad, DateTimeOffset at)
        {
            if (source == Matches)
                await SeedMatchAsync(factory, userId, ad, at, ct);
            else
                await SeedHitAsync(factory, userId, ad, watch, at, ct);
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
            (await MatchRowAsync(factory, userId, during, ct)).ReadInNotificationFeed.ShouldBeTrue();
        else
            (await HitRowsAsync(factory, userId, during, ct)).ShouldAllBe(h => h.ReadInNotificationFeed);
    }

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task Switching_on_when_the_type_is_already_on_marks_nothing(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"backlog-noop-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Ny", ct);
        // Created "in the future" relative to anything a null stamp could mean: with no stamp the backlog is empty, and
        // a null stamp must never read as "since forever".
        if (source == Matches)
            await SeedMatchAsync(factory, userId, ad, DateTimeOffset.UtcNow.AddMinutes(10), ct);
        else
            await SeedHitAsync(
                factory, userId, ad, await SeedWatchAsync(factory, userId, ct), DateTimeOffset.UtcNow.AddMinutes(10), ct);

        (await SwitchAsync(client, source, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        if (source == Matches)
            (await MatchRowAsync(factory, userId, ad, ct)).ReadInNotificationFeed.ShouldBeFalse();
        else
            (await HitRowsAsync(factory, userId, ad, ct)).ShouldAllBe(h => !h.ReadInNotificationFeed);
    }

    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task A_switch_without_enabled_is_a_400_that_writes_nothing(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"switch-400-{source}", ct);
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
        var (client, userId) = await RegisterUserAsync(factory, $"switch-audit-{source}", ct);
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
        var (client, userId) = await RegisterUserAsync(factory, "mark-no-audit", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Rad", ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
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
        var (client, userId) = await RegisterUserAsync(factory, "switch-own-stamp", ct);
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

    // ── the X (#2151, ADR 0169) ──────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> DismissAsync(HttpClient client, string source, Guid adId, CancellationToken ct) =>
        client.PostAsync($"{Feed}/{source}/{adId}/dismiss", content: null, ct);

    /// <summary>The user's X on a match as the command ends it, minus the consent predicate: used only to arrange state.</summary>
    private async Task DismissMatchRowDirectAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.UserJobAdMatches.Where(m => m.UserId == userId && m.JobAdId == adId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(m => m.DismissedFromNotificationFeed, true)
                .SetProperty(m => m.ReadInNotificationFeed, true), ct);
    }

    private async Task DismissHitsDirectAsync(Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var hit in await db.FollowedCompanyAdHits.Where(h => h.UserId == userId && h.JobAdId == adId).ToListAsync(ct))
            hit.DismissFromNotificationFeed();
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task The_dismiss_route_refuses_an_anonymous_caller_a_foreign_source_and_an_empty_id()
    {
        var ct = TestContext.Current.CancellationToken;
        (await factory.CreateClient().PostAsync($"{Feed}/{Matches}/{Guid.NewGuid()}/dismiss", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (client, userId) = await RegisterUserAsync(factory, "dismiss-400", ct);
        await ConsentAsync(userId, true, ct);
        foreach (var source in new[] { "7", "0", "Match", "FollowedCompanyAd", "application" })
            (await DismissAsync(client, source, Guid.NewGuid(), ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DismissAsync(client, Matches, Guid.Empty, ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsync($"{Feed}/{Matches}/not-a-guid/dismiss", null, ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void The_dismiss_route_requires_authorization_and_shares_the_mark_bucket_and_no_route_deletes()
    {
        _ = factory.CreateClient();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var endpoint = routes
            .Where(e => string.Equals(
                            e.RoutePattern.RawText, "/api/v1/me/notification-feed/{source}/{jobAdId:guid}/dismiss", StringComparison.Ordinal)
                        && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"))
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty();
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(RateLimitingExtensions.NotificationFeedMarkPolicy);
        // A DELETE would read as the deletion of match rows that #2138 forbids: no row is deleted, a flag is set.
        routes.Where(e => e.RoutePattern.RawText?.Contains("/notification-feed", StringComparison.Ordinal) == true)
            .ShouldNotContain(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("DELETE"));
    }

    // 204 for every request from a signed-in owner: a missing and a foreign row say nothing about whose they are.
    [Fact]
    public async Task A_dismissal_answers_204_for_a_missing_and_a_foreign_row_and_changes_no_other_users_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var (me, myId) = await RegisterUserAsync(factory, "dismiss-204-me", ct);
        var (_, otherId) = await RegisterUserAsync(factory, "dismiss-204-other", ct);
        await ConsentAsync(myId, true, ct);
        await ConsentAsync(otherId, true, ct);
        var theirsMatch = await SeedAdAsync(factory, "Deras match", ct);
        var theirsFollow = await SeedAdAsync(factory, "Deras följda", ct);
        await SeedMatchAsync(factory, otherId, theirsMatch, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, otherId, theirsFollow, await SeedWatchAsync(factory, otherId, ct), Base.AddMinutes(1), ct);

        foreach (var (source, ad) in new[]
                 {
                     (Matches, Guid.NewGuid()),
                     (Matches, theirsMatch.Value),
                     (FollowedAds, Guid.NewGuid()),
                     (FollowedAds, theirsFollow.Value),
                 })
        {
            (await DismissAsync(me, source, ad, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var theirs = await MatchRowAsync(factory, otherId, theirsMatch, ct);
        theirs.DismissedFromNotificationFeed.ShouldBeFalse();
        theirs.ReadInNotificationFeed.ShouldBeFalse();
        (await HitRowsAsync(factory, otherId, theirsFollow, ct))
            .ShouldAllBe(h => !h.DismissedFromNotificationFeed && !h.ReadInNotificationFeed);
    }

    // ADR 0169: the X removes the notification from the bell and from nothing else. Whatever Översikt and the two list
    // pages show is measured before and after, the rows are still there, and the mail state and the watermark are untouched.
    [Fact]
    public async Task Dismissing_a_match_takes_it_out_of_the_bell_and_leaves_Oversikt_the_list_the_rows_and_the_mail_state_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-match", ct);
        await ConsentAsync(userId, true, ct);
        var gone = await SeedAdAsync(factory, "Borttagen", ct);
        var kept = await SeedAdAsync(factory, "Kvar", ct);
        await SeedMatchAsync(factory, userId, gone, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, kept, Base.AddMinutes(2), ct);
        var before = await MatchRowAsync(factory, userId, gone, ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenMatchesAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/new-match-count", ct);
        Source(await GetFeedAsync(client, ct), "Match").UnreadCount.ShouldBe(2);

        (await DismissAsync(client, Matches, gone.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await MatchRowAsync(factory, userId, gone, ct);
        after.DismissedFromNotificationFeed.ShouldBeTrue();
        after.ReadInNotificationFeed.ShouldBeTrue("a dismissed row is a read row");
        after.NotificationStatus.ShouldBe(before.NotificationStatus);
        after.SentAt.ShouldBe(before.SentAt);
        (await MatchRowAsync(factory, userId, kept, ct)).DismissedFromNotificationFeed.ShouldBeFalse();
        (await SeekerAsync(userId, ct)).LastSeenMatchesAt.ShouldBe(watermark);

        var feed = await GetFeedAsync(client, ct);
        feed.Items.Select(i => i.JobAdId).ToList().ShouldBe([kept.Value]);
        Source(feed, "Match").UnreadCount.ShouldBe(1);
        // The token still covers the dismissed row (ADR 0168 D2: no flag predicate), so "Markera alla" strands nothing.
        Source(feed, "Match").AcknowledgedThrough.ShouldBe(Base.AddMinutes(2));

        // Översikt's count and /matchningar: unchanged, the dismissed match included.
        (await CountAsync(client, "/api/v1/me/new-match-count", ct)).ShouldBe(oversiktBefore);
        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var matches = await new GetMyMatchesQueryHandler(db, currentUser).Handle(new GetMyMatchesQuery(), ct);
        matches.Select(m => m.JobAdId).ToList().ShouldBe([kept.Value, gone.Value], ignoreOrder: true);
        db.UserJobAdMatches.Count(m => m.UserId == userId).ShouldBe(2);
    }

    [Fact]
    public async Task Dismissing_a_followed_ad_takes_every_hit_of_it_out_of_the_bell_and_leaves_Oversikt_the_list_and_the_mail_state_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-follow", ct);
        var (_, otherId) = await RegisterUserAsync(factory, "dismiss-follow-other", ct);
        var ad = await SeedAdAsync(factory, "Följd", ct);
        var elsewhere = await SeedAdAsync(factory, "Annan", ct);
        var watchA = await SeedWatchAsync(factory, userId, ct);
        var watchB = await SeedWatchAsync(factory, userId, ct);
        await SeedHitAsync(factory, userId, ad, watchA, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, ad, watchB, Base.AddMinutes(2), ct);
        await SeedHitAsync(factory, userId, elsewhere, watchA, Base.AddMinutes(3), ct);
        await SeedHitAsync(factory, otherId, ad, await SeedWatchAsync(factory, otherId, ct), Base.AddMinutes(1), ct);
        var watermark = (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt;
        var oversiktBefore = await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct);
        Source(await GetFeedAsync(client, ct), "FollowedCompanyAd").UnreadCount.ShouldBe(2);

        (await DismissAsync(client, FollowedAds, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var hits = await HitRowsAsync(factory, userId, ad, ct);
        hits.Count.ShouldBe(2);
        hits.ShouldAllBe(h => h.DismissedFromNotificationFeed && h.ReadInNotificationFeed);
        hits.ShouldAllBe(h => h.SeenAt == null && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending);
        (await HitRowsAsync(factory, userId, elsewhere, ct)).ShouldAllBe(h => !h.DismissedFromNotificationFeed);
        (await HitRowsAsync(factory, otherId, ad, ct)).ShouldAllBe(h => !h.DismissedFromNotificationFeed && !h.ReadInNotificationFeed);
        (await SeekerAsync(userId, ct)).LastSeenFollowedAdsAt.ShouldBe(watermark);

        var feed = await GetFeedAsync(client, ct);
        feed.Items.Select(i => i.JobAdId).ToList().ShouldBe([elsewhere.Value]);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(1);
        (await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct)).ShouldBe(oversiktBefore);

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));
        var search = Substitute.For<IPerUserJobAdSearchQuery>();
        search.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>());
        var list = await new ListNewFollowedCompanyAdsQueryHandler(db, currentUser, profileBuilder, search)
            .Handle(new ListNewFollowedCompanyAdsQuery(), ct);
        list.Rows.Count.ShouldBe(2);
    }

    // The trap the form round named: a dismissal that filters on the READ flag, as the read mark does, does nothing on a row
    // the user already opened in the bell, which is exactly the row they most want gone.
    [Fact]
    public async Task The_X_works_on_a_row_already_read_in_the_bell_in_both_sources()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-read", ct);
        await ConsentAsync(userId, true, ct);
        var match = await SeedAdAsync(factory, "Läst match", ct);
        var followed = await SeedAdAsync(factory, "Läst följd", ct);
        await SeedMatchAsync(factory, userId, match, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, followed, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);
        await ReadMatchInBellAsync(userId, match, ct);
        await ReadHitsInBellAsync(factory, userId, followed, ct);
        (await GetFeedAsync(client, ct)).Items.Count.ShouldBe(2);

        (await DismissAsync(client, Matches, match.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, FollowedAds, followed.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(factory, userId, match, ct)).DismissedFromNotificationFeed.ShouldBeTrue();
        (await HitRowsAsync(factory, userId, followed, ct)).ShouldAllBe(h => h.DismissedFromNotificationFeed);
        var feed = await GetFeedAsync(client, ct);
        feed.Items.ShouldBeEmpty();
        Source(feed, "Match").UnreadCount.ShouldBe(0);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_dismissal_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-twice", ct);
        await ConsentAsync(userId, true, ct);
        var match = await SeedAdAsync(factory, "M", ct);
        var followed = await SeedAdAsync(factory, "F", ct);
        await SeedMatchAsync(factory, userId, match, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, followed, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);

        for (var i = 0; i < 2; i++)
        {
            (await DismissAsync(client, Matches, match.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await DismissAsync(client, FollowedAds, followed.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await MatchRowAsync(factory, userId, match, ct)).DismissedFromNotificationFeed.ShouldBeTrue();
        (await HitRowsAsync(factory, userId, followed, ct)).Single().DismissedFromNotificationFeed.ShouldBeTrue();
    }

    // #2138 is open: nothing writes a match row while the consent is withdrawn (security V18, V19). The consent is withdrawn
    // through the production endpoint, and the rows differ from the control's only in the consent. The positive control comes
    // first, for an unread and an already-read row, or the negative case would assert on nothing.
    [Fact]
    public async Task A_match_is_dismissed_while_the_consent_is_held_and_not_written_once_it_is_withdrawn()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-consent", ct);
        await ConsentAsync(userId, true, ct);
        var unread = await SeedAdAsync(factory, "Oläst", ct);
        var read = await SeedAdAsync(factory, "Läst", ct);
        var unreadAfterWithdrawal = await SeedAdAsync(factory, "Oläst efter återkallelsen", ct);
        var readAfterWithdrawal = await SeedAdAsync(factory, "Läst efter återkallelsen", ct);
        await SeedMatchAsync(factory, userId, unread, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, read, Base.AddMinutes(2), ct);
        await SeedMatchAsync(factory, userId, unreadAfterWithdrawal, Base.AddMinutes(3), ct);
        await SeedMatchAsync(factory, userId, readAfterWithdrawal, Base.AddMinutes(4), ct);
        await ReadMatchInBellAsync(userId, read, ct);
        await ReadMatchInBellAsync(userId, readAfterWithdrawal, ct);

        // Control: with the consent held both flags are written, for an unread row and for a row already read.
        (await DismissAsync(client, Matches, unread.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, Matches, read.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        foreach (var ad in new[] { unread, read })
        {
            var row = await MatchRowAsync(factory, userId, ad, ct);
            row.DismissedFromNotificationFeed.ShouldBeTrue();
            row.ReadInNotificationFeed.ShouldBeTrue();
        }

        (await client.PutAsJsonAsync("/api/v1/me/background-match-notification-consent", new { enabled = false }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissAsync(client, Matches, unreadAfterWithdrawal.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, Matches, readAfterWithdrawal.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var untouched = await MatchRowAsync(factory, userId, unreadAfterWithdrawal, ct);
        untouched.DismissedFromNotificationFeed.ShouldBeFalse();
        untouched.ReadInNotificationFeed.ShouldBeFalse();
        var untouchedRead = await MatchRowAsync(factory, userId, readAfterWithdrawal, ct);
        untouchedRead.DismissedFromNotificationFeed.ShouldBeFalse();
        untouchedRead.ReadInNotificationFeed.ShouldBeTrue();
    }

    // The follow consent governs e-mail only (ADR 0168 D3): a followed ad is dismissed whatever the match consent says.
    [Fact]
    public async Task A_followed_ad_is_dismissed_whatever_the_match_consent_says()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-follow-consent", ct);
        await ConsentAsync(userId, false, ct);
        var ad = await SeedAdAsync(factory, "Följd", ct);
        await SeedHitAsync(factory, userId, ad, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);

        (await DismissAsync(client, FollowedAds, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await HitRowsAsync(factory, userId, ad, ct)).Single().DismissedFromNotificationFeed.ShouldBeTrue();
    }

    // The type's own switch is not the gate (ADR 0168 D3): an X on a row the bell can no longer show still works.
    [Fact]
    public async Task The_X_still_writes_while_the_type_is_switched_off()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-type-off", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Match", ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        (await SwitchAsync(client, Matches, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissAsync(client, Matches, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MatchRowAsync(factory, userId, ad, ct)).DismissedFromNotificationFeed.ShouldBeTrue();
    }

    // Dismissal is one-way (ADR 0169 D3'): no writer clears it, and the CTO asked for a test per writer. The five writers that
    // can reach a row are exercised below: "Markera alla" and the read mark here, the switch-on backlog mark, a list visit
    // (the two watermark endpoints and the per-hit seen stamp), and the e-mail path.
    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task A_dismissed_notification_never_comes_back_through_mark_all_or_a_read_mark(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"dismiss-oneway-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var gone = await SeedAdAsync(factory, "Borttagen", ct);
        var other = await SeedAdAsync(factory, "Annan", ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        if (source == Matches)
        {
            await SeedMatchAsync(factory, userId, gone, Base.AddMinutes(1), ct);
            await SeedMatchAsync(factory, userId, other, Base.AddMinutes(2), ct);
        }
        else
        {
            await SeedHitAsync(factory, userId, gone, watch, Base.AddMinutes(1), ct);
            await SeedHitAsync(factory, userId, other, watch, Base.AddMinutes(2), ct);
        }

        (await DismissAsync(client, source, gone.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var kind = source == Matches ? "Match" : "FollowedCompanyAd";
        var token = Source(await GetFeedAsync(client, ct), kind).AcknowledgedThrough!.Value;
        (await MarkThroughAsync(client, source, token, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkRowAsync(client, source, gone.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissedFlagsAsync(userId, source, gone, ct)).ShouldAllBe(d => d);
        (await GetFeedAsync(client, ct)).Items.ShouldNotContain(i => i.JobAdId == gone.Value);
    }

    // The switch-on backlog mark writes the rows created AFTER the type's off-stamp, which is the real clock: so the row is seeded
    // after the stamp (the actor that produced it is the producer job, which writes created_at from its own clock), dismissed with
    // the X while the type is off (the X works then, ADR 0168 D3), and the switch is turned on. The control is a second row that
    // arrived while off and was not dismissed: the backlog mark must read THAT one, or it never reached the first.
    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task A_dismissed_notification_stays_dismissed_when_the_switch_on_backlog_mark_runs(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"dismiss-backlog-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        (await SwitchAsync(client, source, false, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var gone = await SeedAdAsync(factory, "Kom medan den var av, borttagen", ct);
        var arrived = await SeedAdAsync(factory, "Kom medan den var av", ct);
        var afterTheStamp = DateTimeOffset.UtcNow.AddMinutes(10);
        if (source == Matches)
        {
            await SeedMatchAsync(factory, userId, gone, afterTheStamp, ct);
            await SeedMatchAsync(factory, userId, arrived, afterTheStamp.AddMinutes(1), ct);
        }
        else
        {
            await SeedHitAsync(factory, userId, gone, watch, afterTheStamp, ct);
            await SeedHitAsync(factory, userId, arrived, watch, afterTheStamp.AddMinutes(1), ct);
        }
        (await DismissAsync(client, source, gone.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SwitchAsync(client, source, true, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissedFlagsAsync(userId, source, gone, ct)).ShouldAllBe(d => d);
        // The control: the backlog mark did run over this type's rows, and read the one that was not dismissed.
        var arrivedRead = source == Matches
            ? [(await MatchRowAsync(factory, userId, arrived, ct)).ReadInNotificationFeed]
            : (await HitRowsAsync(factory, userId, arrived, ct)).Select(h => h.ReadInNotificationFeed).ToList();
        arrivedRead.ShouldAllBe(r => r, "the backlog mark reaches the rows created after the off-stamp");
        (await GetFeedAsync(client, ct)).Items.ShouldNotContain(i => i.JobAdId == gone.Value);
    }

    // A list visit: the two watermark endpoints /matchningar and /foretag/bevakade/nya call, and the per-hit seen stamp an ad
    // open sends. None of them may clear the flag, and none of them may be what the X depends on.
    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task A_dismissed_notification_stays_dismissed_through_a_list_visit_and_the_seen_stamp(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"dismiss-visit-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var gone = await SeedAdAsync(factory, "Borttagen", ct);
        if (source == Matches)
            await SeedMatchAsync(factory, userId, gone, Base.AddMinutes(1), ct);
        else
            await SeedHitAsync(factory, userId, gone, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);
        (await DismissAsync(client, source, gone.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = Base.AddMinutes(5).ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync(
                "/api/v1/me/followed-company-ads/seen", new { seenThrough = Base.AddMinutes(5).ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync($"/api/v1/me/company-watches/ad-hits/{gone.Value}/seen", null, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissedFlagsAsync(userId, source, gone, ct)).ShouldAllBe(d => d);
        (await GetFeedAsync(client, ct)).Items.ShouldNotContain(i => i.JobAdId == gone.Value);
    }

    // The e-mail path, at the database: the mail dispatchers load a row with tracking and mutate the aggregate. An entity that
    // was loaded BEFORE the user's X and is saved AFTER it must not write the stale dismissed=false back: EF writes the changed
    // columns only (ADR 0168 s8, lapse trigger 8 of ADR 0169 names the day this stops being true). The aggregate cannot reach
    // dismissed=true itself, so only a database-level test can hold this.
    [Fact]
    public async Task A_mail_transition_saved_after_the_X_leaves_the_notification_dismissed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-mail", ct);
        await ConsentAsync(userId, true, ct);
        var matchAd = await SeedAdAsync(factory, "Match", ct);
        var followAd = await SeedAdAsync(factory, "Följd", ct);
        await SeedMatchAsync(factory, userId, matchAd, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, followAd, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var match = await db.UserJobAdMatches.SingleAsync(m => m.UserId == userId && m.JobAdId == matchAd, ct);
        var hit = await db.FollowedCompanyAdHits.SingleAsync(h => h.UserId == userId && h.JobAdId == followAd, ct);

        (await DismissAsync(client, Matches, matchAd.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, FollowedAds, followAd.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var clock = new FakeDateTimeProvider(Base.AddMinutes(10));
        match.MarkQueued().IsSuccess.ShouldBeTrue();
        match.MarkSent(clock).IsSuccess.ShouldBeTrue();
        hit.MarkQueued().IsSuccess.ShouldBeTrue();
        hit.MarkSent(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var savedMatch = await MatchRowAsync(factory, userId, matchAd, ct);
        savedMatch.NotificationStatus.ShouldBe(NotificationStatus.Sent);
        savedMatch.DismissedFromNotificationFeed.ShouldBeTrue();
        savedMatch.ReadInNotificationFeed.ShouldBeTrue();
        var savedHit = (await HitRowsAsync(factory, userId, followAd, ct)).Single();
        savedHit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
        savedHit.DismissedFromNotificationFeed.ShouldBeTrue();
        savedHit.ReadInNotificationFeed.ShouldBeTrue();
    }

    // The architect's Viktigt 1: the X must work on a row the WATERMARK has already read (the user visited the list), where the
    // read flag is false. A dismissal that copied MarkThroughAsync's watermark predicate would be a 204 with no effect on every
    // row the user has already seen on /matchningar or /foretag/bevakade/nya, and every other test here would stay green.
    [Theory]
    [InlineData(Matches)]
    [InlineData(FollowedAds)]
    public async Task The_X_works_on_a_row_the_watermark_already_read(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, $"dismiss-watermark-{source}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "Redan sedd på listan", ct);
        if (source == Matches)
            await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        else
            await SeedHitAsync(factory, userId, ad, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);
        var seenPath = source == Matches ? "/api/v1/me/matches/seen" : "/api/v1/me/followed-company-ads/seen";
        (await client.PostAsJsonAsync(seenPath, new { seenThrough = Base.AddMinutes(2).ToString("O") }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var listed = (await GetFeedAsync(client, ct)).Items.Single(i => i.JobAdId == ad.Value);
        listed.IsUnread.ShouldBeFalse("the watermark read it; the bell still lists it");
        var flagsBefore = source == Matches
            ? [(await MatchRowAsync(factory, userId, ad, ct)).ReadInNotificationFeed]
            : (await HitRowsAsync(factory, userId, ad, ct)).Select(h => h.ReadInNotificationFeed).ToList();
        flagsBefore.ShouldAllBe(r => !r, "read through the watermark, not in the bell");

        (await DismissAsync(client, source, ad.Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await DismissedFlagsAsync(userId, source, ad, ct)).ShouldAllBe(d => d);
        (await GetFeedAsync(client, ct)).Items.ShouldNotContain(i => i.JobAdId == ad.Value);
    }

    private async Task<List<bool>> DismissedFlagsAsync(Guid userId, string source, JobAdId ad, CancellationToken ct) =>
        source == Matches
            ? [(await MatchRowAsync(factory, userId, ad, ct)).DismissedFromNotificationFeed]
            : (await HitRowsAsync(factory, userId, ad, ct)).Select(h => h.DismissedFromNotificationFeed).ToList();

    [Fact]
    public async Task The_bells_count_is_Oversikts_count_minus_the_rows_read_or_dismissed_in_the_bell_when_no_followed_ad_is_a_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "dismiss-parity", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var matchAds = new List<JobAdId>();
        var followAds = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var m = await SeedAdAsync(factory, $"M{i}", ct);
            var f = await SeedAdAsync(factory, $"F{i}", ct);
            matchAds.Add(m);
            followAds.Add(f);
            await SeedMatchAsync(factory, userId, m, Base.AddMinutes(1 + i), ct);
            await SeedHitAsync(factory, userId, f, watch, Base.AddMinutes(1 + i), ct);
        }

        // matchAds[0]: read, then dismissed (counts once); matchAds[1]: dismissed unread; followAds[2]: dismissed unread.
        await ReadMatchInBellAsync(userId, matchAds[0], ct);
        (await DismissAsync(client, Matches, matchAds[1].Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, Matches, matchAds[0].Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(client, FollowedAds, followAds[2].Value, ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var feed = await GetFeedAsync(client, ct);
        Source(feed, "Match").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/new-match-count", ct) - 2);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct) - 1);
        feed.Items.Count.ShouldBe(4 + 4 - 3);
    }

    // The filter is in the window's SQL, not applied after it: dismissed rows newer than an unread one neither hide it nor
    // trigger the shortfall statements (ADR 0168 D6), so the request stays at nine statements, and a dismissed hit takes no
    // slot of the follow window and moves no horizon.
    [Fact]
    public async Task Dismissed_rows_do_not_crowd_out_an_older_unread_one_and_cost_no_extra_statement()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "dismiss-crowd", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var unreadMatch = await SeedAdAsync(factory, "Oläst match", ct);
        var unreadFollow = await SeedAdAsync(factory, "Oläst följd", ct);
        await SeedMatchAsync(factory, userId, unreadMatch, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, unreadFollow, watch, Base.AddMinutes(1), ct);
        for (var i = 0; i < 8; i++)
        {
            var match = await SeedAdAsync(factory, $"Borttagen match {i}", ct);
            await SeedMatchAsync(factory, userId, match, Base.AddMinutes(10 + i), ct);
            await DismissMatchRowDirectAsync(userId, match, ct);
            var followed = await SeedAdAsync(factory, $"Borttagen följd {i}", ct);
            await SeedHitAsync(factory, userId, followed, watch, Base.AddMinutes(10 + i), ct);
            await DismissHitsDirectAsync(userId, followed, ct);
        }

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var context = new NotificationFeedReadContext(
            db, userId, MaxItems: 2, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

        var matches = await new MatchFeedSource().ReadAsync(context, ct);
        var followed2 = await new FollowedCompanyAdFeedSource().ReadAsync(context, ct);

        matches.Items.Select(i => i.JobAdId).ToList().ShouldBe([unreadMatch.Value]);
        followed2.Items.Select(i => i.JobAdId).ToList().ShouldBe([unreadFollow.Value]);
        matches.UnreadCount.ShouldBe(1);
        followed2.UnreadCount.ShouldBe(1);
        // Match: consent + watermark, token, count, list. Followed: watermark, watches, token, count, window.
        counter.Count.ShouldBe(9);
        followed2.Horizon.ShouldBeNull();
    }

    // The invariant at the database (ADR 0169): a writer that sets the dismissed flag without the read flag fails loudly.
    [Theory]
    [InlineData("user_job_ad_matches", "ck_user_job_ad_matches_dismissed_implies_read")]
    [InlineData("followed_company_ad_hits", "ck_followed_company_ad_hits_dismissed_implies_read")]
    public async Task A_dismissed_row_that_is_not_read_is_refused_by_the_database(string table, string constraint)
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, $"check-{table}", ct);
        await ConsentAsync(userId, true, ct);
        var ad = await SeedAdAsync(factory, "A", ct);
        if (table == "user_job_ad_matches")
            await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), ct);
        else
            await SeedHitAsync(factory, userId, ad, await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
#pragma warning disable EF1002 // table is one of the two literals in the InlineData, never input
        var thrown = await Should.ThrowAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET dismissed_from_notification_feed = true, read_in_notification_feed = false WHERE user_id = {{0}}",
            [userId], ct));
#pragma warning restore EF1002
        thrown.ConstraintName.ShouldBe(constraint);
    }

    // ── the read paths on Npgsql ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_older_unread_match_behind_twenty_newer_rows_read_in_the_bell_is_listed_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "crowd-match", ct);
        await ConsentAsync(userId, true, ct);
        var unread = await SeedAdAsync(factory, "Oläst", ct);
        await SeedMatchAsync(factory, userId, unread, Base.AddMinutes(1), ct);
        for (var i = 0; i < 21; i++)
        {
            var read = await SeedAdAsync(factory, $"Läst {i}", ct);
            await SeedMatchAsync(factory, userId, read, Base.AddMinutes(10 + i), ct);
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
        var (_, userId) = await RegisterUserAsync(factory, "crowd-follow", ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var unread = await SeedAdAsync(factory, "Oläst följd", ct);
        await SeedHitAsync(factory, userId, unread, watch, Base.AddMinutes(1), ct);
        var newestUnread = await SeedAdAsync(factory, "Nyaste olästa följd", ct);
        await SeedHitAsync(factory, userId, newestUnread, watch, Base.AddMinutes(2), ct);
        for (var i = 0; i < 10; i++)
        {
            var read = await SeedAdAsync(factory, $"Läst följd {i}", ct);
            await SeedHitAsync(factory, userId, read, watch, Base.AddMinutes(10 + i), ct);
            await ReadHitsInBellAsync(factory, userId, read, ct);
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
        var (_, userId) = await RegisterUserAsync(factory, "crowd-cost", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "Oläst match", ct), Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "Oläst följd", ct), watch, Base.AddMinutes(1), ct);
        for (var i = 0; i < 8; i++)
        {
            var match = await SeedAdAsync(factory, $"Läst match {i}", ct);
            await SeedMatchAsync(factory, userId, match, Base.AddMinutes(10 + i), ct);
            await ReadMatchInBellAsync(userId, match, ct);
            var followed = await SeedAdAsync(factory, $"Läst följd {i}", ct);
            await SeedHitAsync(factory, userId, followed, watch, Base.AddMinutes(10 + i), ct);
            await ReadHitsInBellAsync(factory, userId, followed, ct);
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

    [Fact]
    public async Task The_bells_count_is_Oversikts_count_minus_exactly_the_rows_read_in_the_bell_when_no_followed_ad_is_a_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "parity-flags", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var matchAds = new List<JobAdId>();
        var followAds = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var m = await SeedAdAsync(factory, $"M{i}", ct);
            var f = await SeedAdAsync(factory, $"F{i}", ct);
            matchAds.Add(m);
            followAds.Add(f);
            await SeedMatchAsync(factory, userId, m, Base.AddMinutes(1 + i), ct);
            await SeedHitAsync(factory, userId, f, watch, Base.AddMinutes(1 + i), ct);
        }

        var feed = await GetFeedAsync(client, ct);
        Source(feed, "Match").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/new-match-count", ct));
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct));

        await ReadMatchInBellAsync(userId, matchAds[0], ct);
        await ReadMatchInBellAsync(userId, matchAds[2], ct);
        await ReadHitsInBellAsync(factory, userId, followAds[1], ct);

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
        var (_, userId) = await RegisterUserAsync(factory, "sql-flag", ct);
        await ConsentAsync(userId, true, ct);
        var watch = await SeedWatchAsync(factory, userId, ct);
        var onlyMatched = await SeedWatchAsync(factory, userId, onlyMatched: true, ct);
        var matchAd = await SeedAdAsync(factory, "M", ct);
        var followAd = await SeedAdAsync(factory, "F", ct);
        await SeedMatchAsync(factory, userId, matchAd, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, followAd, watch, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "G", ct), onlyMatched, Base.AddMinutes(2), ct);

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
        // #2151 (security V21): the dismissed flag is the bell's own as well.
        statements.Texts.ShouldNotContain(text => text.Contains("dismissed_from_notification_feed", StringComparison.OrdinalIgnoreCase));
    }

    // The positive control of the pin above: the interceptor does see the bell's own statements, and they DO name the dismissed
    // flag (the two window statements, ADR 0169 D1'), so the pin cannot pass because it watches nothing.
    [Fact]
    public async Task The_bells_own_window_statements_name_the_dismissed_flag()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "sql-dismissed-control", ct);
        await ConsentAsync(userId, true, ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "M", ct), Base.AddMinutes(1), ct);
        await SeedHitAsync(
            factory, userId, await SeedAdAsync(factory, "F", ct), await SeedWatchAsync(factory, userId, ct), Base.AddMinutes(1), ct);

        using var scope = factory.Services.CreateScope();
        var statements = new StatementText();
        await using var db = NewContext(scope, statements);
        var context = new NotificationFeedReadContext(
            db, userId, MaxItems: 20, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

        await new MatchFeedSource().ReadAsync(context, ct);
        var afterMatches = statements.Texts.Count(t => t.Contains("dismissed_from_notification_feed", StringComparison.OrdinalIgnoreCase));
        await new FollowedCompanyAdFeedSource().ReadAsync(context, ct);
        var afterBoth = statements.Texts.Count(t => t.Contains("dismissed_from_notification_feed", StringComparison.OrdinalIgnoreCase));

        afterMatches.ShouldBe(1, "the match list statement");
        afterBoth.ShouldBe(2, "the follow window statement");
    }

    // The grade path over Npgsql with EXCLUDED hits (#2130 PR 1's named skip, closed here): an "endast matchade" watch's
    // newest hits grade below Good and fill the window, so the older ads that do match are named only by the fallback, which
    // runs the counted-hits-to-ads read the in-memory tests cannot translate. The grader is the one stub, as a profile
    // build and a search belong to other contexts; everything else is the real provider.
    [Fact]
    public async Task On_the_grade_path_hits_the_grader_excludes_fill_the_window_and_the_older_matching_unread_ads_are_still_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "grade-exclusion", ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: true, ct);
        var matching = new List<JobAdId>();
        for (var i = 1; i <= 3; i++)
        {
            var ad = await SeedAdAsync(factory, $"Matchande {i}", ct);
            matching.Add(ad);
            await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(i), ct);
        }

        const int maxItems = 3;
        var window = maxItems * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 1; i++)
            await SeedHitAsync(
                factory, userId, await SeedAdAsync(factory, $"Utesluten {i}", ct), watch, Base.AddHours(1).AddMinutes(i), ct);

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
}
