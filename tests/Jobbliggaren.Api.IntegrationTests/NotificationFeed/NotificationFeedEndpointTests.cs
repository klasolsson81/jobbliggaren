using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
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
    private const string FeedEndpoint = "/api/v1/me/notification-feed";

    private sealed record FeedItem(
        string Kind, Guid JobAdId, string Title, string CompanyName, DateTimeOffset OccurredAt, bool IsUnread, string? Grade);

    private sealed record FeedSource(string Kind, int? UnreadCount, DateTimeOffset? AcknowledgedThrough);

    private sealed record Feed(IReadOnlyList<FeedItem> Items, IReadOnlyList<FeedSource> Sources);

    private sealed record CountResponse(int Count);

    // Whole seconds in the past: the /seen setters clamp to now, and a seeded instant must survive
    // timestamptz's microsecond resolution unchanged.
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

    private async Task SetBackgroundMatchConsentAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.UpdateNotificationConsent(enabled, scope.ServiceProvider.GetRequiredService<IDateTimeProvider>());
        await db.SaveChangesAsync(ct);
    }

    private async Task<JobAdId> SeedAdAsync(string title, string company, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FakeDateTimeProvider(Base.AddHours(-1));
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
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

    private async Task SeedMatchAsync(
        Guid userId, JobAdId adId, NotifiableMatchGrade grade, DateTimeOffset at, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserJobAdMatches.Add(UserJobAdMatch.Create(userId, adId, grade, ["csharp"], new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    private async Task<CompanyWatchId> SeedWatchAsync(Guid userId, bool onlyMatched, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(NewOrgNr()).Value, clock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    private async Task SeedHitAsync(
        Guid userId, JobAdId adId, CompanyWatchId watchId, DateTimeOffset at, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, adId, watchId, new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    private static string NewOrgNr() =>
        "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<Feed> GetFeedAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(FeedEndpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var feed = await response.Content.ReadFromJsonAsync<Feed>(ct);
        feed.ShouldNotBeNull();
        return feed;
    }

    private static FeedSource Source(Feed feed, string kind) => feed.Sources.Single(s => s.Kind == kind);

    private static async Task<int> GetCountAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CountResponse>(ct))!.Count;
    }

    // ── the surface ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_anonymous_request_is_refused_with_401()
    {
        var response = await factory.CreateClient().GetAsync(FeedEndpoint, TestContext.Current.CancellationToken);

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
        var (client, userId) = await RegisterUserAsync("feed-shape", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var matchedAd = await SeedAdAsync("Utvecklare", "Match AB", ct);
        var followedAd = await SeedAdAsync("Testare", "Följt AB", ct);
        await SeedMatchAsync(userId, matchedAd, NotifiableMatchGrade.Top, Base.AddMinutes(1), ct);
        var watch = await SeedWatchAsync(userId, onlyMatched: false, ct);
        await SeedHitAsync(userId, followedAd, watch, Base.AddMinutes(2), ct);

        var response = await client.GetAsync(FeedEndpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        var feed = (await response.Content.ReadFromJsonAsync<Feed>(ct))!;

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
        var (mine, me) = await RegisterUserAsync("feed-me", ct);
        var (_, other) = await RegisterUserAsync("feed-other", ct);
        await SetBackgroundMatchConsentAsync(me, enabled: true, ct);
        await SetBackgroundMatchConsentAsync(other, enabled: true, ct);
        var theirMatch = await SeedAdAsync("Deras", "Annan AB", ct);
        var theirFollow = await SeedAdAsync("Deras följda", "Annan AB", ct);
        await SeedMatchAsync(other, theirMatch, NotifiableMatchGrade.Strong, Base.AddMinutes(1), ct);
        await SeedHitAsync(other, theirFollow, await SeedWatchAsync(other, onlyMatched: false, ct), Base.AddMinutes(2), ct);
        var myAd = await SeedAdAsync("Min", "Mitt AB", ct);
        await SeedMatchAsync(me, myAd, NotifiableMatchGrade.Good, Base.AddMinutes(3), ct);

        var feed = await GetFeedAsync(mine, ct);

        feed.Items.Select(i => i.JobAdId).ShouldBe([myAd.Value]);
        Source(feed, "Match").ShouldBe(new FeedSource("Match", 1, Base.AddMinutes(3)));
        Source(feed, "FollowedCompanyAd").ShouldBe(new FeedSource("FollowedCompanyAd", 0, null));
    }

    // B1 (security-auditor, #1666 form round; ADR 0080 Beslut 5): the bell is a notification surface,
    // and a withdrawal leaves the match rows in place.
    [Fact]
    public async Task Withdrawing_background_match_consent_silences_the_match_source_and_granting_it_restores_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("feed-consent", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var ad = await SeedAdAsync("Roll", "Bolag AB", ct);
        await SeedMatchAsync(userId, ad, NotifiableMatchGrade.Top, Base.AddMinutes(1), ct);
        Source(await GetFeedAsync(client, ct), "Match").UnreadCount.ShouldBe(1);

        await SetBackgroundMatchConsentAsync(userId, enabled: false, ct);
        var silenced = await GetFeedAsync(client, ct);
        Source(silenced, "Match").ShouldBe(new FeedSource("Match", 0, null));
        silenced.Items.ShouldNotContain(i => i.Kind == "Match");
        (await GetCountAsync(client, "/api/v1/me/new-match-count", ct))
            .ShouldBe(1, "the rows stay; only the bell goes quiet");

        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var restored = await GetFeedAsync(client, ct);
        Source(restored, "Match").UnreadCount.ShouldBe(1);
        restored.Items.ShouldContain(i => i.Kind == "Match" && i.JobAdId == ad.Value);
    }

    // The bell's numbers are the existing definitions, not new ones. The seed carries what separates
    // them: an archived ad in each source, an ad reached through two follows, and an "endast matchade"
    // watch (this user states no occupation, so its filter is inert and every hit is included).
    [Fact]
    public async Task Each_sources_unread_count_equals_the_existing_new_count()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("feed-parity", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var archivedMatch = await SeedAdAsync("Arkiverad", "Bolag AB", ct);
        await SeedMatchAsync(userId, await SeedAdAsync("A", "Bolag AB", ct), NotifiableMatchGrade.Good, Base.AddMinutes(1), ct);
        await SeedMatchAsync(userId, await SeedAdAsync("B", "Bolag AB", ct), NotifiableMatchGrade.Strong, Base.AddMinutes(2), ct);
        await SeedMatchAsync(userId, archivedMatch, NotifiableMatchGrade.Top, Base.AddMinutes(3), ct);
        await ArchiveAsync(archivedMatch, ct);

        var plain = await SeedWatchAsync(userId, onlyMatched: false, ct);
        var onlyMatched = await SeedWatchAsync(userId, onlyMatched: true, ct);
        var twice = await SeedAdAsync("Två vägar", "Följt AB", ct);
        var archivedHit = await SeedAdAsync("Arkiverad följd", "Följt AB", ct);
        await SeedHitAsync(userId, twice, plain, Base.AddMinutes(4), ct);
        await SeedHitAsync(userId, twice, onlyMatched, Base.AddMinutes(5), ct);
        await SeedHitAsync(userId, await SeedAdAsync("Endast matchade", "Följt AB", ct), onlyMatched, Base.AddMinutes(6), ct);
        await SeedHitAsync(userId, archivedHit, plain, Base.AddMinutes(7), ct);
        await ArchiveAsync(archivedHit, ct);

        var feed = await GetFeedAsync(client, ct);

        Source(feed, "Match").UnreadCount.ShouldBe(await GetCountAsync(client, "/api/v1/me/new-match-count", ct));
        Source(feed, "Match").UnreadCount.ShouldBe(2);
        Source(feed, "FollowedCompanyAd").UnreadCount
            .ShouldBe(await GetCountAsync(client, "/api/v1/me/followed-company-ads/new-count", ct));
        Source(feed, "FollowedCompanyAd").UnreadCount.ShouldBe(2);
        feed.Items.Count(i => i.IsUnread).ShouldBe(4);
        feed.Items.ShouldNotContain(i => i.JobAdId == archivedMatch.Value || i.JobAdId == archivedHit.Value);
    }

    // The tokens are what "Markera alla som lästa" sends to the existing /seen endpoints, unchanged.
    // Microsecond instants pin that a token survives the wire exactly: a token rounded down would
    // leave its own row unread and the badge would never reach zero.
    [Fact]
    public async Task Sending_the_tokens_to_the_existing_seen_endpoints_clears_both_counts_and_a_later_row_stays_unread()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterUserAsync("feed-ack", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        var watch = await SeedWatchAsync(userId, onlyMatched: false, ct);
        await SeedMatchAsync(userId, await SeedAdAsync("M", "Bolag AB", ct), NotifiableMatchGrade.Good,
            Base.AddMinutes(1).AddTicks(1_230), ct);
        await SeedHitAsync(userId, await SeedAdAsync("F", "Bolag AB", ct), watch, Base.AddMinutes(2).AddTicks(4_560), ct);

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

        await SeedMatchAsync(userId, await SeedAdAsync("Senare", "Bolag AB", ct), NotifiableMatchGrade.Top,
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
        var (_, userId) = await RegisterUserAsync("feed-race-match", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        await SeedMatchAsync(userId, await SeedAdAsync("Först", "Bolag AB", ct), NotifiableMatchGrade.Good, Base.AddMinutes(1), ct);
        var late = await SeedAdAsync("Sen", "Bolag AB", ct);

        using var scope = factory.Services.CreateScope();
        var interceptor = new AfterFirstMaxInterceptor(
            () => SeedMatchAsync(userId, late, NotifiableMatchGrade.Top, Base.AddMinutes(2), ct));
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
        var (_, userId) = await RegisterUserAsync("feed-race-follow", ct);
        var watch = await SeedWatchAsync(userId, onlyMatched: false, ct);
        await SeedHitAsync(userId, await SeedAdAsync("Först", "Bolag AB", ct), watch, Base.AddMinutes(1), ct);
        var late = await SeedAdAsync("Sen", "Bolag AB", ct);

        using var scope = factory.Services.CreateScope();
        var interceptor = new AfterFirstMaxInterceptor(
            () => SeedHitAsync(userId, late, watch, Base.AddMinutes(2), ct));
        await using var db = NewContext(scope, interceptor);

        var read = await new FollowedCompanyAdFeedSource().ReadAsync(Context(db, userId), ct);

        interceptor.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(Base.AddMinutes(1));
        read.Items.Select(i => i.JobAdId).ShouldNotContain(late.Value);

        await using var fresh = NewContext(scope);
        (await new FollowedCompanyAdFeedSource().ReadAsync(Context(fresh, userId), ct)).UnreadCount.ShouldBe(2);
    }

    // The per-request cost the rate-limit bucket was sized on (RateLimitingOptions.NotificationFeedRead):
    // with no "endast matchade" watch nothing is graded, and both sources run their full read.
    [Fact]
    public async Task One_request_without_an_only_matched_watch_runs_nine_statements()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, userId) = await RegisterUserAsync("feed-cost", ct);
        await SetBackgroundMatchConsentAsync(userId, enabled: true, ct);
        await SeedMatchAsync(userId, await SeedAdAsync("M", "Bolag AB", ct), NotifiableMatchGrade.Good, Base.AddMinutes(1), ct);
        await SeedHitAsync(userId, await SeedAdAsync("F", "Bolag AB", ct),
            await SeedWatchAsync(userId, onlyMatched: false, ct), Base.AddMinutes(2), ct);

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

    /// <summary>
    /// A fresh <see cref="AppDbContext"/> on the fixture's database with only the given interceptors —
    /// the <c>FilterToMatchingQueryPlanTests</c> idiom. None of the tables read here carries a
    /// DEK-encrypted column, so the field-encryption interceptor pair is not needed.
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

    /// <summary>Commits a row through another context right after the first <c>MAX</c> statement returns.</summary>
    private sealed class AfterFirstMaxInterceptor(Func<Task> commit) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("max(", StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                await commit();
            }

            return result;
        }
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
}
