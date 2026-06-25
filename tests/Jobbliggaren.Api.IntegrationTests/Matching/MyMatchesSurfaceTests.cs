using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;
using Jobbliggaren.Application.Matching.Queries.GetMyMatches;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — the dedicated "Mina matchningar"-surface (GetMyMatches +
/// GetMyNewMatchCount + MarkMatchesSeen) against REAL Postgres (Testcontainers). The bits
/// InMemory hides and ONLY a relational provider honours:
/// <list type="bullet">
/// <item>the handler-managed JOIN <c>UserJobAdMatch.JobAdId equals JobAd.Id</c> across the
/// strongly-typed <see cref="JobAdId"/>/<c>uuid</c> converter (an inner join — a match whose
/// ad is soft-deleted/absent is DROPPED, no FK);</item>
/// <item>the <c>CreatedAt &gt; LastSeenMatchesAt</c> watermark comparison + the
/// <c>IsNew</c> in-memory projection over the relational fetch;</item>
/// <item>the soft-delete <c>HasQueryFilter(DeletedAt == null)</c> on BOTH aggregates;</item>
/// <item>the watermark ROUND-TRIP (MarkMatchesSeen persists → the new-count drops to 0).</item>
/// </list>
/// Handler-level tests resolve the REAL handler from DI with a substituted
/// <see cref="ICurrentUser"/> for the seeded UserId (parity
/// <see cref="MatchProfileBuilderFullCvIntegrationTests"/>); the endpoint smoke tests drive
/// the wired HTTP surface (parity <see cref="MeMatchCountEndpointTests"/>). Each test uses a
/// fresh random UserId so rows never collide across the shared [Collection("Api")] table.
/// </summary>
[Collection("Api")]
public sealed class MyMatchesSurfaceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // The deterministic seed clock (matches are stamped relative to this).
    private static readonly DateTimeOffset T0 =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private (AppDbContext Db, IDateTimeProvider Clock, IServiceScope Scope) NewScope()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        return (db, clock, scope);
    }

    private static ICurrentUser UserWith(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    // A clock pinned to a chosen instant (UserJobAdMatch.Create stamps CreatedAt = clock.UtcNow).
    private static IDateTimeProvider ClockAt(DateTimeOffset at)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(at);
        return clock;
    }

    // Seeds an Active JobAd with known Title/Company/Url; returns its id.
    private static async Task<JobAdId> SeedJobAdAsync(
        AppDbContext db, IDateTimeProvider clock, string title, string company, string url,
        CancellationToken ct)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        // Import requires a non-empty rawPayload (JobAd.RawPayloadRequired); a minimal JSON
        // object suffices — this suite asserts the join/order/IsNew, not payload-derived columns.
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: url,
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            publishedAt: T0,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    // Seeds a UserJobAdMatch (CreatedAt stamped at `createdAt`) joining the user to an ad.
    private static async Task SeedMatchAsync(
        AppDbContext db, Guid userId, JobAdId jobAdId, NotifiableMatchGrade grade,
        DateTimeOffset createdAt, CancellationToken ct)
    {
        var match = UserJobAdMatch.Create(
            userId, jobAdId, grade, ["csharp"], ClockAt(createdAt)).Value;
        db.UserJobAdMatches.Add(match);
        await db.SaveChangesAsync(ct);
    }

    // Seeds a JobSeeker for the user, optionally with the watermark set to `lastSeen`.
    private static async Task SeedSeekerAsync(
        AppDbContext db, Guid userId, DateTimeOffset? lastSeen, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Surface User", ClockAt(T0)).Value;
        if (lastSeen is { } seen)
            seeker.SetLastSeenMatches(ClockAt(seen));
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
    }

    // ===============================================================
    // 3. GetMyMatches — JOIN + IsNew + cap + order + inner-join drop.
    // ===============================================================

    [Fact]
    public async Task GetMyMatches_JoinsAdDetails_OrdersByCreatedAtDesc_AndComputesIsNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            // Watermark at day 5: matches BEFORE → IsNew false; AFTER → IsNew true.
            await SeedSeekerAsync(db, userId, lastSeen: T0.AddDays(5), ct);

            var topAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Toppmatch-annons", "Topp AB", "https://example.com/top", ct);
            var strongAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Stark-annons", "Stark AB", "https://example.com/strong", ct);
            var goodAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Bra-annons", "Bra AB", "https://example.com/good", ct);

            // day 8 (after watermark → IsNew), day 6 (after → IsNew), day 2 (before → not new).
            await SeedMatchAsync(db, userId, topAd, NotifiableMatchGrade.Top, T0.AddDays(8), ct);
            await SeedMatchAsync(db, userId, strongAd, NotifiableMatchGrade.Strong, T0.AddDays(6), ct);
            await SeedMatchAsync(db, userId, goodAd, NotifiableMatchGrade.Good, T0.AddDays(2), ct);

            var handler = new GetMyMatchesQueryHandler(db, UserWith(userId));
            var result = await handler.Handle(new GetMyMatchesQuery(), ct);

            result.Count.ShouldBe(3);

            // Order: CreatedAt descending → Top(8), Strong(6), Good(2).
            result[0].JobAdId.ShouldBe(topAd.Value);
            result[1].JobAdId.ShouldBe(strongAd.Value);
            result[2].JobAdId.ShouldBe(goodAd.Value);

            // The JOIN surfaces the ad's public details (no CV content).
            result[0].Title.ShouldBe("Toppmatch-annons");
            result[0].Company.ShouldBe("Topp AB");
            result[0].Url.ShouldBe("https://example.com/top");
            result[0].Grade.ShouldBe(NotifiableMatchGrade.Top);

            // IsNew computed against the day-5 watermark.
            result[0].IsNew.ShouldBeTrue("day 8 > watermark day 5");
            result[1].IsNew.ShouldBeTrue("day 6 > watermark day 5");
            result[2].IsNew.ShouldBeFalse("day 2 <= watermark day 5");
        }
    }

    [Fact]
    public async Task GetMyMatches_StillSurfacesFailedNotificationMatch_StatusAgnostic()
    {
        // TD-114 — a match whose notification stranded and was reaped to Failed is STILL a real
        // match the user should see; the read surface does NOT filter on NotificationStatus
        // (delivery status != match validity). Regression lock against a future status filter
        // that would hide stranded matches from /matchningar. Real Postgres (the Failed value
        // round-trips through the varchar(20) by-name conversion).
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            await SeedSeekerAsync(db, userId, lastSeen: null, ct);
            var ad = await SeedJobAdAsync(
                db, ClockAt(T0), "Strandad-annons", "Strand AB", "https://example.com/stranded", ct);

            var match = UserJobAdMatch.Create(
                userId, ad, NotifiableMatchGrade.Top, ["csharp"], ClockAt(T0.AddDays(1))).Value;
            match.MarkQueued();
            match.MarkFailed();
            db.UserJobAdMatches.Add(match);
            await db.SaveChangesAsync(ct);

            var handler = new GetMyMatchesQueryHandler(db, UserWith(userId));
            var result = await handler.Handle(new GetMyMatchesQuery(), ct);

            result.Count.ShouldBe(1);
            result[0].JobAdId.ShouldBe(ad.Value);
            result[0].Grade.ShouldBe(NotifiableMatchGrade.Top);
        }
    }

    [Fact]
    public async Task GetMyMatches_DropsMatch_WhenItsJobAdIsAbsentOrFilteredOut()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            await SeedSeekerAsync(db, userId, lastSeen: null, ct);

            var liveAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Levande-annons", "Live AB", "https://example.com/live", ct);

            // A match to a live ad, and a match to a JobAdId that was NEVER inserted (a dangling
            // reference — UserJobAdMatch holds the JobAdId by IDENTITY, no FK, ADR 0058/0059).
            // The handler's INNER join to JobAds (which itself carries
            // HasQueryFilter(DeletedAt == null)) drops the dangling match — a stale link is
            // never surfaced, even though the match row physically exists.
            await SeedMatchAsync(db, userId, liveAd, NotifiableMatchGrade.Strong, T0.AddDays(3), ct);
            await SeedMatchAsync(db, userId, JobAdId.New(), NotifiableMatchGrade.Top, T0.AddDays(1), ct);

            // Both match rows exist (proven by the raw count); only the joinable one surfaces.
            // Fully-qualify the EF extension to dodge the .NET 10
            // System.Linq.AsyncEnumerable-vs-EF overload ambiguity (CS0411) on DbSet.
            var rawMatchCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.UserJobAdMatches.Where(m => m.UserId == userId), ct);
            rawMatchCount.ShouldBe(2, "both match rows were persisted; the join decides visibility");

            var handler = new GetMyMatchesQueryHandler(db, UserWith(userId));
            var result = await handler.Handle(new GetMyMatchesQuery(), ct);

            // Only the live-ad match survives the inner join.
            result.Count.ShouldBe(1);
            result[0].JobAdId.ShouldBe(liveAd.Value);
            result[0].Title.ShouldBe("Levande-annons");
        }
    }

    [Fact]
    public async Task GetMyMatches_CapsAtFiftyMostRecent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            await SeedSeekerAsync(db, userId, lastSeen: null, ct);

            // 55 matches each to its own ad — the handler caps at 50, keeping the most recent.
            const int total = 55;
            for (var i = 0; i < total; i++)
            {
                var ad = await SeedJobAdAsync(
                    db, ClockAt(T0), $"Annons {i}", $"Company {i}", $"https://example.com/{i}", ct);
                await SeedMatchAsync(db, userId, ad, NotifiableMatchGrade.Good, T0.AddDays(i), ct);
            }

            var handler = new GetMyMatchesQueryHandler(db, UserWith(userId));
            var result = await handler.Handle(new GetMyMatchesQuery(), ct);

            result.Count.ShouldBe(50, "the view caps at the 50 most recent matches");
            // Most recent first → the newest seeded (day 54) leads; the oldest 5 are dropped.
            result[0].Title.ShouldBe($"Annons {total - 1}");
            result.ShouldNotContain(r => r.Title == "Annons 0");
            result.ShouldNotContain(r => r.Title == "Annons 4");
        }
    }

    // ===============================================================
    // 4. GetMyNewMatchCount reflects the watermark; MarkMatchesSeen drops it to 0.
    // ===============================================================

    [Fact]
    public async Task GetMyNewMatchCount_CountsMatchesAfterWatermark_AndDropsToZeroAfterMarkSeen()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            // Watermark at day 5; 3 matches after it (days 6,7,8), 1 before (day 2).
            await SeedSeekerAsync(db, userId, lastSeen: T0.AddDays(5), ct);
            var adA = await SeedJobAdAsync(db, ClockAt(T0), "A", "A AB", "https://example.com/a", ct);
            var adB = await SeedJobAdAsync(db, ClockAt(T0), "B", "B AB", "https://example.com/b", ct);
            var adC = await SeedJobAdAsync(db, ClockAt(T0), "C", "C AB", "https://example.com/c", ct);
            var adD = await SeedJobAdAsync(db, ClockAt(T0), "D", "D AB", "https://example.com/d", ct);
            await SeedMatchAsync(db, userId, adA, NotifiableMatchGrade.Good, T0.AddDays(6), ct);
            await SeedMatchAsync(db, userId, adB, NotifiableMatchGrade.Strong, T0.AddDays(7), ct);
            await SeedMatchAsync(db, userId, adC, NotifiableMatchGrade.Top, T0.AddDays(8), ct);
            await SeedMatchAsync(db, userId, adD, NotifiableMatchGrade.Good, T0.AddDays(2), ct);

            var countHandler = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));
            var before = await countHandler.Handle(new GetMyNewMatchCountQuery(), ct);
            before.Count.ShouldBe(3, "3 matches created after the day-5 watermark");

            // Advance the watermark to NOW (a future instant past every match) via the real
            // command handler; persist (UnitOfWorkBehavior is bypassed when calling directly).
            var markHandler = new MarkMatchesSeenCommandHandler(
                db, UserWith(userId), ClockAt(T0.AddDays(20)));
            var markResult = await markHandler.Handle(new MarkMatchesSeenCommand(), ct);
            markResult.IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);

            // The watermark round-trips (FRESH scope proves it came from the DB) → new-count 0.
            var (readDb, _, readScope) = NewScope();
            using (readScope)
            {
                var after = new GetMyNewMatchCountQueryHandler(readDb, UserWith(userId));
                var afterCount = await after.Handle(new GetMyNewMatchCountQuery(), ct);
                afterCount.Count.ShouldBe(0,
                    "after MarkMatchesSeen advanced the watermark past every match, none are new");
            }
        }
    }

    // ===============================================================
    // 5. THE COHERENCE LOOP — new-count == #IsNew items in GetMyMatches for the same
    //    user + watermark (the Översikt count matches what the view highlights).
    // ===============================================================

    [Fact]
    public async Task NewMatchCount_EqualsNumberOfIsNewItemsInGetMyMatches_ForSameUserAndWatermark()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            // Watermark at day 5. A mixed distribution straddling it.
            await SeedSeekerAsync(db, userId, lastSeen: T0.AddDays(5), ct);
            int[] days = [9, 8, 7, 6, 5, 4, 3]; // 4 after (9,8,7,6), 1 at (5), 2 before (4,3)
            for (var i = 0; i < days.Length; i++)
            {
                var ad = await SeedJobAdAsync(
                    db, ClockAt(T0), $"Annons d{days[i]}", $"Co {i}", $"https://example.com/{i}", ct);
                await SeedMatchAsync(db, userId, ad, NotifiableMatchGrade.Good, T0.AddDays(days[i]), ct);
            }

            var countHandler = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));
            var listHandler = new GetMyMatchesQueryHandler(db, UserWith(userId));

            var count = await countHandler.Handle(new GetMyNewMatchCountQuery(), ct);
            var list = await listHandler.Handle(new GetMyMatchesQuery(), ct);

            var isNewInList = list.Count(r => r.IsNew);

            // The load-bearing coherence invariant: the Översikt "Nya matchningar"-number is
            // EXACTLY the number of rows the view highlights as new (same user, same fetch-time
            // watermark, strict CreatedAt > LastSeenMatchesAt on both paths).
            count.Count.ShouldBe(isNewInList,
                "the new-match count must equal the number of IsNew items the matches view shows");
            count.Count.ShouldBe(4, "days 6,7,8,9 are strictly after the day-5 watermark");
        }
    }

    // ===============================================================
    // 6. Endpoint smoke — auth gate + JSON contract over the wired HTTP surface.
    // ===============================================================

    [Fact]
    public async Task GET_new_match_count_authed_returns_200_with_count_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.GetAsync("/api/v1/me/new-match-count", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("count", out var countProp).ShouldBeTrue(
            "the response carries a `count` field (camelCase) — the contract the FE notis reads");
        countProp.ValueKind.ShouldBe(JsonValueKind.Number);
        // A fresh user (registration auto-creates a JobSeeker with a null watermark) has no
        // background matches yet → honest 0.
        countProp.GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task GET_matches_authed_returns_200_with_json_array()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.GetAsync("/api/v1/me/matches", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array, "GET /me/matches returns a JSON array");
        json.GetArrayLength().ShouldBe(0, "a fresh user has no background matches yet");
    }

    [Fact]
    public async Task POST_matches_seen_authed_returns_204_then_new_match_count_is_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // The registered user already has a JobSeeker (RegisterCommandHandler auto-provisions
        // one) → MarkMatchesSeen succeeds with 204 (not NotFound/400).
        var seen = await client.PostAsync("/api/v1/me/matches/seen", content: null, ct);
        seen.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // After advancing the watermark, the new-match count is 0 (no matches arrived since).
        var countResponse = await client.GetAsync("/api/v1/me/new-match-count", ct);
        countResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await countResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task POST_matches_seen_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        // No Authorization header → RequireAuthorization rejects before the handler runs.
        var response = await client.PostAsync("/api/v1/me/matches/seen", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_matches_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me/matches", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_new_match_count_anonymous_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me/new-match-count", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
