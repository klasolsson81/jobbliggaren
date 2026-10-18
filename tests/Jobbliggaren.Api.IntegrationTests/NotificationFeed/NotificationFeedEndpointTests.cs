using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
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
/// #1666 — <c>GET /api/v1/me/notification-feed</c> end to end against Testcontainers Postgres. Only a
/// real provider proves the sources' statements translate (the SmartEnum <c>Status</c> join, the
/// value-converted id joins, <c>MAX</c> over a join, the bounded probe), and only the wired stack
/// proves the surface: 401, owner scope, <c>private, no-store</c>, string enums, the consent gate,
/// count parity with the two existing new-counts, and that the returned tokens acknowledge exactly
/// what was counted through the existing <c>/seen</c> endpoints. Every seed goes through an
/// aggregate; every test uses fresh users (the shared <c>[Collection("Api")]</c> database is never reset).
/// </summary>
[Collection("Api")]
public class NotificationFeedEndpointTests(ApiFactory factory)
{
    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    private async Task SetBackgroundMatchConsentAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        await db.SaveChangesAsync(ct);
    }

    private async Task UnfollowAsync(CompanyWatchId watchId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watch = await db.CompanyWatches.SingleAsync(w => w.Id == watchId, ct);
        watch.SoftDelete(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        await db.SaveChangesAsync(ct);
    }

    private static string[] Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    // ── the surface ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_request_is_refused_with_401()
    {
        var response = await factory.CreateClient().GetAsync(FeedPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void The_route_requires_authorization_and_carries_its_own_rate_limit_policy()
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("/notification-feed", StringComparison.Ordinal))
            // #2130 added the read marks and the two switches under the same prefix; their wiring is
            // NotificationFeedReadStateEndpointTests'. This test is the feed read, the one GET.
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"))
            .ShouldHaveSingleItem();

        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(["GET"]);
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty();
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName
            .ShouldBe(RateLimitingExtensions.NotificationFeedReadPolicy);
    }

    [Fact]
    public async Task The_feed_reads_both_sources_as_strings_and_is_never_cached()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-shape", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var matchedAd = await SeedAdAsync(factory, "Utvecklare", "Match AB", ct);
        var followedAd = await SeedAdAsync(factory, "Testare", "Följt AB", ct);
        await SeedMatchAsync(factory, userId, matchedAd, NotifiableMatchGrade.Top, Base.AddMinutes(1), ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        await SeedHitAsync(factory, userId, followedAd, watch, Base.AddMinutes(2), ct);

        var response = await client.GetAsync(FeedPath, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync(ct);
        var feed = JsonSerializer.Deserialize<FeedBody>(body, JsonSerializerOptions.Web)!;

        // The wire is an allow-list: nothing beyond these names leaves the API (no org.nr, watch id or URL).
        using var json = JsonDocument.Parse(body);
        Names(json.RootElement).ShouldBe(["items", "sources"]);
        Names(json.RootElement.GetProperty("items")[0])
            .ShouldBe(["companyName", "grade", "isUnread", "jobAdId", "kind", "occurredAt", "title"]);
        Names(json.RootElement.GetProperty("sources")[0]).ShouldBe(["acknowledgedThrough", "kind", "unreadCount"]);

        feed.Items.Select(i => (i.Kind, i.JobAdId)).ShouldBe(
        [
            ("FollowedCompanyAd", followedAd.Value),
            ("Match", matchedAd.Value),
        ]);
        var match = feed.Items.Single(i => i.Kind == "Match");
        match.Grade.ShouldBe("Top");
        match.Title.ShouldBe("Utvecklare");
        match.CompanyName.ShouldBe("Match AB");
        match.IsUnread.ShouldBeTrue();
        feed.Items.Single(i => i.Kind == "FollowedCompanyAd").Grade.ShouldBeNull();
        Source(feed, "Match").ShouldBe(new FeedSource("Match", 1, Base.AddMinutes(1)));
        Source(feed, "FollowedCompanyAd").ShouldBe(new FeedSource("FollowedCompanyAd", 1, Base.AddMinutes(2)));
    }

    [Fact]
    public async Task One_user_never_sees_another_users_rows_counts_or_tokens()
    {
        var ct = TestContext.Current.CancellationToken;
        var (mine, me) = await RegisterUserAsync(factory, "feed-me", ct);
        var (_, other) = await RegisterUserAsync(factory, "feed-other", ct);
        await SetBackgroundMatchConsentAsync(me, enabled: true, ct);
        await SetBackgroundMatchConsentAsync(other, enabled: true, ct);
        var theirMatch = await SeedAdAsync(factory, "Deras", "Annan AB", ct);
        var theirFollow = await SeedAdAsync(factory, "Deras följda", "Annan AB", ct);
        await SeedMatchAsync(factory, other, theirMatch, NotifiableMatchGrade.Strong, Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, other, theirFollow, await SeedWatchAsync(factory, other, onlyMatched: false, ct),
            Base.AddMinutes(2), ct);
        // My rows are newer than theirs, so theirs fall inside my list bound: only the owner filters keep them out.
        var myAd = await SeedAdAsync(factory, "Min", "Mitt AB", ct);
        var myFollow = await SeedAdAsync(factory, "Min följda", "Mitt AB", ct);
        await SeedMatchAsync(factory, me, myAd, NotifiableMatchGrade.Good, Base.AddMinutes(3), ct);
        await SeedHitAsync(factory, me, myFollow, await SeedWatchAsync(factory, me, onlyMatched: false, ct),
            Base.AddMinutes(4), ct);

        var feed = await GetFeedAsync(mine, ct);

        feed.Items.Select(i => i.JobAdId).ShouldBe([myFollow.Value, myAd.Value]);
        Source(feed, "Match").ShouldBe(new FeedSource("Match", 1, Base.AddMinutes(3)));
        Source(feed, "FollowedCompanyAd").ShouldBe(new FeedSource("FollowedCompanyAd", 1, Base.AddMinutes(4)));
    }

    // B1 (security-auditor, #1666 form round; ADR 0080 Beslut 5): the bell is a notification surface,
    // and a withdrawal leaves the match rows in place.
    [Fact]
    public async Task Withdrawing_background_match_consent_silences_the_match_source_and_granting_it_restores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-consent", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var ad = await SeedAdAsync(factory, "Roll", "Bolag AB", ct);
        await SeedMatchAsync(factory, userId, ad, NotifiableMatchGrade.Top, Base.AddMinutes(1), ct);
        Source(await GetFeedAsync(client, ct), "Match").UnreadCount.ShouldBe(1);

        await SetBackgroundMatchConsentAsync(userId, enabled: false, ct);
        var silenced = await GetFeedAsync(client, ct);
        Source(silenced, "Match").ShouldBe(new FeedSource("Match", 0, null));
        silenced.Items.ShouldNotContain(i => i.Kind == "Match");
        (await CountAsync(client, "/api/v1/me/new-match-count", ct))
            .ShouldBe(1, "the rows stay; only the bell goes quiet");

        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var restored = await GetFeedAsync(client, ct);
        Source(restored, "Match").UnreadCount.ShouldBe(1);
        restored.Items.ShouldContain(i => i.Kind == "Match" && i.JobAdId == ad.Value);
    }

    // The seed carries what separates them: an archived ad in each source, an ad reached through two
    // follows, and an "endast matchade" watch (this user states no occupation, so its filter is inert
    // and every hit is included).
    [Fact]
    public async Task Each_sources_unread_count_equals_the_existing_new_count_when_no_followed_ad_is_a_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-parity", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        // Every excluded row is OLDER than the live ones, so the token bound cannot hide it from the list.
        var archivedMatch = await SeedAdAsync(factory, "Arkiverad", "Bolag AB", ct);
        await SeedMatchAsync(factory, userId, archivedMatch, NotifiableMatchGrade.Top, Base.AddMinutes(1), ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "A", "Bolag AB", ct), NotifiableMatchGrade.Good,
            Base.AddMinutes(2), ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "B", "Bolag AB", ct), NotifiableMatchGrade.Strong,
            Base.AddMinutes(3), ct);
        await ArchiveAsync(factory, archivedMatch, ct);

        var plain = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        var onlyMatched = await SeedWatchAsync(factory, userId, onlyMatched: true, ct);
        var unfollowed = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        var twice = await SeedAdAsync(factory, "Två vägar", "Följt AB", ct);
        var archivedHit = await SeedAdAsync(factory, "Arkiverad följd", "Följt AB", ct);
        var ofUnfollowed = await SeedAdAsync(factory, "Avföljd", "Följt AB", ct);
        await SeedHitAsync(factory, userId, archivedHit, plain, Base.AddMinutes(4), ct);
        await SeedHitAsync(factory, userId, ofUnfollowed, unfollowed, Base.AddMinutes(5), ct);
        await SeedHitAsync(factory, userId, twice, plain, Base.AddMinutes(6), ct);
        await SeedHitAsync(factory, userId, twice, onlyMatched, Base.AddMinutes(7), ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "Endast matchade", "Följt AB", ct), onlyMatched,
            Base.AddMinutes(8), ct);
        await ArchiveAsync(factory, archivedHit, ct);
        await UnfollowAsync(unfollowed, ct);

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "Match").UnreadCount.ShouldBe(await CountAsync(client, "/api/v1/me/new-match-count", ct));
        Source(feed, "Match").UnreadCount.ShouldBe(2);
        Source(feed, "FollowedCompanyAd").UnreadCount
            .ShouldBe(await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct));
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(2);
        feed.Items.Count(i => i.IsUnread).ShouldBe(4);
        feed.Items.ShouldNotContain(i =>
            i.JobAdId == archivedMatch.Value || i.JobAdId == archivedHit.Value || i.JobAdId == ofUnfollowed.Value);
    }

    // The tokens are what "Markera alla som lästa" sends to the existing /seen endpoints, unchanged.
    // Microsecond instants pin that a token survives the wire exactly: a token rounded down would
    // leave its own row unread and the badge would never reach zero.
    [Fact]
    public async Task Sending_the_tokens_to_the_existing_seen_endpoints_clears_both_counts_and_a_later_row_stays_unread()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-ack", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "M", "Bolag AB", ct), NotifiableMatchGrade.Good,
            Base.AddMinutes(1).AddTicks(1_230), ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "F", "Bolag AB", ct), watch,
            Base.AddMinutes(2).AddTicks(4_560), ct);

        var before = await GetFeedAsync(client, ct);
        var matchToken = Source(before, "Match").AcknowledgedThrough!.Value;
        var followToken = Source(before, "FollowedCompanyAd").AcknowledgedThrough!.Value;
        matchToken.ShouldBe(Base.AddMinutes(1).AddTicks(1_230));

        (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = matchToken }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/api/v1/me/followed-company-ads/seen", new { seenThrough = followToken }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var acknowledged = await GetFeedAsync(client, ct);
        acknowledged.Sources.ShouldAllBe(s => s.UnreadCount == 0 && s.AcknowledgedThrough == null);
        acknowledged.Items.Count.ShouldBe(2);
        acknowledged.Items.ShouldAllBe(i => !i.IsUnread);

        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "Senare", "Bolag AB", ct), NotifiableMatchGrade.Top,
            matchToken.AddTicks(10), ct);
        var later = await GetFeedAsync(client, ct);
        Source(later, "Match").UnreadCount.ShouldBe(1);
        later.Items.Single(i => i.IsUnread).Kind.ShouldBe("Match");
    }

    // ── the token protocol under a concurrent writer ─────────────────────────────────────────

    // Token first, then count and list bounded by it: a row committed between the token and the rest
    // is neither counted nor listed, so it stays unread for the next poll instead of being acknowledged
    // unseen. The interceptor commits the row right after the source's MAX statement returns.
    [Fact]
    public async Task A_match_committed_after_the_token_is_neither_counted_nor_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "feed-race-match", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "Först", "Bolag AB", ct), NotifiableMatchGrade.Good,
            Base.AddMinutes(1), ct);
        var late = await SeedAdAsync(factory, "Sen", "Bolag AB", ct);

        using var scope = factory.Services.CreateScope();
        var interceptor = new AfterFirstMaxInterceptor(
            () => SeedMatchAsync(factory, userId, late, NotifiableMatchGrade.Top, Base.AddMinutes(2), ct));
        await using var db = NewContext(scope, interceptor);

        var read = await new MatchFeedSource().ReadAsync(Context(db, userId), ct);

        interceptor.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(Base.AddMinutes(1));
        read.Items.Select(i => i.JobAdId).ShouldNotContain(late.Value);

        await using var fresh = NewContext(scope);
        (await new MatchFeedSource().ReadAsync(Context(fresh, userId), ct)).UnreadCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_followed_hit_committed_after_the_token_is_neither_counted_nor_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "feed-race-follow", ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "Först", "Bolag AB", ct), watch, Base.AddMinutes(1), ct);
        var late = await SeedAdAsync(factory, "Sen", "Bolag AB", ct);

        using var scope = factory.Services.CreateScope();
        var interceptor = new AfterFirstMaxInterceptor(
            () => SeedHitAsync(factory, userId, late, watch, Base.AddMinutes(2), ct));
        await using var db = NewContext(scope, interceptor);

        var read = await new FollowedCompanyAdFeedSource().ReadAsync(Context(db, userId), ct);

        interceptor.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(Base.AddMinutes(1));
        read.Items.Select(i => i.JobAdId).ShouldNotContain(late.Value);

        await using var fresh = NewContext(scope);
        (await new FollowedCompanyAdFeedSource().ReadAsync(Context(fresh, userId), ct)).UnreadCount.ShouldBe(2);
    }

    // ── an unread row is never pushed out of the panel (#2130, ADR 0162 D4 as amended) ──────

    // The badge counts three older unread follows, so the panel lists them: twenty-five newer READ matches
    // cannot take their places. Before the unread-first selection the footer links were the only way out.
    [Fact]
    public async Task An_older_unread_followed_ad_is_listed_beside_twenty_newer_read_matches()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-unread-first", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        var followed = new List<JobAdId>();
        for (var i = 1; i <= 3; i++)
        {
            var ad = await SeedAdAsync(factory, $"Följd {i}", "Följt AB", ct);
            followed.Add(ad);
            await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(i), ct);
        }
        for (var i = 1; i <= 25; i++)
            await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, $"Match {i}", "Match AB", ct),
                NotifiableMatchGrade.Good, Base.AddHours(2).AddMinutes(i), ct);
        // The user has looked at the matches: they are read, the follows are not.
        var matchToken = Source(await GetFeedAsync(client, ct), "Match").AcknowledgedThrough!.Value;
        (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = matchToken }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "Match").UnreadCount.ShouldBe(0);
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(3);
        feed.Items.Count.ShouldBe(20);
        feed.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).Order()
            .ShouldBe(followed.Select(a => a.Value).Order());
    }

    // The follow source's window reads its newest hits, READ ONES INCLUDED, so a horizon exists for every
    // user with enough hits. It used to cut an unread match older than the horizon out of the panel too.
    [Fact]
    public async Task An_unread_match_older_than_the_follow_horizon_is_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-horizon", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var unreadMatch = await SeedAdAsync(factory, "Äldre oläst match", "Match AB", ct);
        await SeedMatchAsync(factory, userId, unreadMatch, NotifiableMatchGrade.Strong, Base.AddMinutes(1), ct);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: false, ct);
        var window = GetMyNotificationFeedQueryHandler.MaxItems * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 2; i++)
            await SeedHitAsync(factory, userId, await SeedAdAsync(factory, $"Följd {i}", "Följt AB", ct), watch,
                Base.AddHours(1).AddMinutes(i), ct);
        // The follows are read; the match is not.
        var followToken = Source(await GetFeedAsync(client, ct), "FollowedCompanyAd").AcknowledgedThrough!.Value;
        (await client.PostAsJsonAsync("/api/v1/me/followed-company-ads/seen", new { seenThrough = followToken }, ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "Match").UnreadCount.ShouldBe(1);
        feed.Items.ShouldContain(i => i.JobAdId == unreadMatch.Value && i.IsUnread);
        feed.Items.Count.ShouldBe(20);
    }

    // The grade path's window is the newest 60 hits. Four hits per ad make that fewer than twenty ads while
    // twenty-one are unread, so the missing ones are listed from the counted hits through one more
    // hits-to-ads read. Only a real provider proves that read translates (this user states no occupation,
    // so the "endast matchade" filter is inert and every hit is included).
    [Fact]
    public async Task The_grade_path_lists_an_unread_ad_beyond_a_window_that_four_hits_per_ad_fill()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync(factory, "feed-grade-window", ct);
        CompanyWatchId[] watches =
        [
            await SeedWatchAsync(factory, userId, onlyMatched: true, ct),
            await SeedWatchAsync(factory, userId, onlyMatched: false, ct),
            await SeedWatchAsync(factory, userId, onlyMatched: false, ct),
            await SeedWatchAsync(factory, userId, onlyMatched: false, ct),
        ];
        for (var ad = 1; ad <= 21; ad++)
        {
            var adId = await SeedAdAsync(factory, $"Annons {ad}", "Följt AB", ct);
            for (var w = 0; w < watches.Length; w++)
                await SeedHitAsync(factory, userId, adId, watches[w], Base.AddMinutes(ad * 10).AddSeconds(w), ct);
        }

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(21);
        feed.Items.Count.ShouldBe(20);
        feed.Items.ShouldAllBe(i => i.IsUnread);
        // The window's 60 newest hits cover ads 21..7; ads 6..2 come from the extra read; ad 1 is past the cap.
        feed.Items.Select(i => i.Title).ShouldBe(Enumerable.Range(2, 20).Reverse().Select(n => $"Annons {n}"));
    }

    // The per-request cost the rate-limit bucket was sized on (RateLimitingOptions.NotificationFeedRead):
    // with no "endast matchade" watch nothing is graded, and both sources run their full read.
    [Fact]
    public async Task One_request_without_an_only_matched_watch_runs_nine_statements()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync(factory, "feed-cost", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "M", "Bolag AB", ct), NotifiableMatchGrade.Good,
            Base.AddMinutes(1), ct);
        await SeedHitAsync(factory, userId, await SeedAdAsync(factory, "F", "Bolag AB", ct),
            await SeedWatchAsync(factory, userId, onlyMatched: false, ct), Base.AddMinutes(2), ct);

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        var handler = new GetMyNotificationFeedQueryHandler(
            db, currentUser, profileBuilder, Substitute.For<IPerUserJobAdSearchQuery>());

        var feed = await handler.Handle(new GetMyNotificationFeedQuery(), ct);

        feed.Items.Count.ShouldBe(2);
        // Match: consent + watermark, token, count, list. Followed: watermark, watches, token, count, window.
        counter.Count.ShouldBe(9);
        await profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    private static NotificationFeedReadContext Context(AppDbContext db, Guid userId) =>
        new(db, userId, GetMyNotificationFeedQueryHandler.MaxItems,
            Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());
}
