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
using Jobbliggaren.TestSupport;
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
/// strongly-typed <see cref="JobAdId"/>/<c>uuid</c> converter (an inner join — a match whose ad
/// ROW IS ABSENT is DROPPED, no FK);</item>
/// <item>the LIFECYCLE gate <c>j.Status == JobAdStatus.Active</c> on BOTH halves (#864) — it
/// crosses the SmartEnum <c>HasConversion</c>, and a comparison that does not translate is
/// invisible under InMemory;</item>
/// <item>the <c>CreatedAt &gt; LastSeenMatchesAt</c> watermark comparison + the
/// <c>IsNew</c> in-memory projection over the relational fetch;</item>
/// <item>NO soft-delete filter on <c>UserJobAdMatch</c> — #868 retired its writerless axis (the
/// <c>DeletedAt</c> column + <c>HasQueryFilter</c> are gone, same disease as #821's <c>JobAd</c>
/// axis). What excludes a stale match is the <c>Status == Active</c> lifecycle gate (#864), pinned
/// below — never a soft-delete filter. (This list once said "on BOTH aggregates", believed a filter
/// was excluding archived ads, seeded only Active ads, and so could never observe that an ARCHIVED
/// ad joins — the #864 defect.)</item>
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
            facets: TestFacets.FromPayload($"{{\"id\":\"{externalId}\"}}"),
            publishedAt: T0,
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;
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
            seeker.SetLastSeenMatches(seen, ClockAt(seen));
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
            // The handler's INNER join to JobAds drops the dangling match — a stale link is never
            // surfaced, even though the match row physically exists.
            //
            // The previous version of this comment credited the drop to JobAd's
            // HasQueryFilter(DeletedAt == null). #821 retired that filter (it never had a writer).
            // The inner join drops a match only when the AD ROW ITSELF IS ABSENT — which is why an
            // ARCHIVED ad, whose row is very much present, sailed straight through it until #864.
            // That gate is now an explicit Status predicate, pinned below.
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

    // ===============================================================
    // #864 — the lifecycle axis, on BOTH halves of the surface at once.
    //
    // This suite is the divergence oracle for the pair (list + badge). It seeded only ACTIVE ads, so
    // it was silent on the one axis where the two halves could disagree — the same blindness #864
    // found in MatchCountOracleTests. Its own docstring credited the exclusion to a soft-delete query
    // filter that #821 retired. The guard's claim was broader than its reach, and the hole it left
    // open was live in production: /matchningar listed archived ads with a grade and a working link.
    //
    // The badge is the sharper half. It joined NOTHING — it counted match rows. Gating the list alone
    // would have rendered "3 nya matchningar" above a view showing zero rows: a count that promises
    // more than its set can deliver, manufactured BY the fix. So the assertion is deliberately made on
    // BOTH halves in ONE test: they must agree, and they must agree on the presentable set.
    //
    // Real Postgres, not InMemory: `j.Status == JobAdStatus.Active` crosses the SmartEnum
    // HasConversion, and a translation that dies at runtime is invisible under InMemory (the trap that
    // killed the strongly-typed-VO Contains form). This test IS the translation proof.
    // ===============================================================

    [Fact]
    public async Task MyMatchesSurface_ExcludesArchivedAd_FromBothTheListAndTheBadge()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            await SeedSeekerAsync(db, userId, lastSeen: null, ct); // null watermark → every match is new

            // ASYMMETRIC seed (2 live + 1 archived), deliberately. The LIST asserts row identity, so it
            // pins gate polarity by itself — but the BADGE is a count-only DTO that cannot say WHICH
            // rows it counted, and with a 1+1 seed an INVERTED badge gate (== Archived) also reads
            // exactly 1, agreeing with the list by coincidence. 2+1 separates every badge state:
            // gate correct → 2, deleted → 3, inverted → 1.
            var liveAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Aktiv-annons", "Live AB", "https://example.com/live", ct);
            var secondLiveAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Aktiv-annons-2", "Live Två AB", "https://example.com/live-2", ct);
            var archivedAd = await SeedJobAdAsync(
                db, ClockAt(T0), "Arkiverad-annons", "Gone AB", "https://example.com/gone", ct);

            await SeedMatchAsync(db, userId, liveAd, NotifiableMatchGrade.Strong, T0.AddDays(3), ct);
            await SeedMatchAsync(db, userId, secondLiveAd, NotifiableMatchGrade.Good, T0.AddDays(1), ct);
            await SeedMatchAsync(db, userId, archivedAd, NotifiableMatchGrade.Top, T0.AddDays(2), ct);

            // Archive through the DOMAIN transition production uses (ExpireJobAdsJob writes exactly this
            // status). No fabricated column state — that fiction (#843) is what let the old exclusion
            // tests stay green while proving nothing, and it is forbidden by #864's AC 4.
            var ad = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstAsync(db.JobAds, j => j.Id == archivedAd, ct);
            ad.Archive(ClockAt(T0.AddDays(4))).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);

            // ALL THREE match rows are physically present. The gate — not the data — decides visibility.
            var rawMatchCount = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.UserJobAdMatches.Where(m => m.UserId == userId), ct);
            rawMatchCount.ShouldBe(3, "alla tre match-raderna finns kvar — grinden, inte datan, avgör synlighet");

            var list = await new GetMyMatchesQueryHandler(db, UserWith(userId))
                .Handle(new GetMyMatchesQuery(), ct);
            var badge = await new GetMyNewMatchCountQueryHandler(db, UserWith(userId))
                .Handle(new GetMyNewMatchCountQuery(), ct);

            // NON-VACUITY FIRST: the ACTIVE matches must still surface, by IDENTITY. Without this, "the
            // archived one is absent" would pass on a join that returns nothing at all — the exact way
            // an exclusion test can be green and worthless.
            list.Count.ShouldBe(2);
            list.ShouldContain(m => m.JobAdId == liveAd.Value && m.Title == "Aktiv-annons");
            list.ShouldContain(m => m.JobAdId == secondLiveAd.Value && m.Title == "Aktiv-annons-2");
            list.ShouldNotContain(m => m.JobAdId == archivedAd.Value,
                "en arkiverad annons får inte listas med en grad och en levande länk — i en LISTA är " +
                "graden en rekommendation, och den är falsk för en annons du inte kan söka");

            // THE COHERENCE CLAIM: the badge counts what the list shows. 2, never 3 — and never 1,
            // which is what an INVERTED badge gate would read.
            badge.Count.ShouldBe(2,
                "badgen måste räkna samma presenterbara mängd som listan visar — annars säger Översikten " +
                "'3 nya matchningar' över en vy som renderar 2 rader");
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
            // null SeenThrough → the handler falls back to clock-now (here day-20), the same
            // "advance past everything" intent this test relies on.
            var markHandler = new MarkMatchesSeenCommandHandler(
                db, UserWith(userId), ClockAt(T0.AddDays(20)));
            var markResult = await markHandler.Handle(new MarkMatchesSeenCommand(null), ct);
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

            // The coherence invariant BELOW the 50-cap: with fewer than 50 new matches the Översikt
            // "Nya matchningar"-number equals exactly the number of rows the view highlights as new
            // (same user, same fetch-time watermark, strict CreatedAt > LastSeenMatchesAt on both
            // paths). ABOVE the cap the count stays the honest UNCAPPED total while the list shows a
            // bounded window — that #273 contract is pinned separately by
            // NewMatchCount_ExceedsTheFiftyCappedList_WhenUserHasMoreThanFiftyNewMatches below. Here
            // 7 (< 50) matches keep the two equal.
            count.Count.ShouldBe(isNewInList,
                "below the 50-cap the new-match count equals the IsNew items the matches view shows");
            count.Count.ShouldBe(4, "days 6,7,8,9 are strictly after the day-5 watermark");
        }
    }

    // ===============================================================
    // 5b. THE DIVERGENCE ORACLE (#273) — for a heavy user the UNCAPPED new-count exceeds the
    //     50-capped /matchningar list. Locks the documented contract: GetMyNewMatchCount is the
    //     true new-match cardinality (the Översikt "Nya matchningar"-badge) and must NOT be clamped
    //     to the list cap; GetMyMatches is a bounded recent-VIEW (the remainder reachable via the
    //     /jobb grade-filter). A future "fix the divergence" clamp trips THIS test, which points
    //     the maintainer back at the GetMyNewMatchCount contract doc.
    // ===============================================================

    [Fact]
    public async Task NewMatchCount_ExceedsTheFiftyCappedList_WhenUserHasMoreThanFiftyNewMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            // Null watermark (never opened the view) → every match is "new". 53 matches > the
            // 50-row list cap, each to its own ad (the inner join keeps all 53 joinable).
            await SeedSeekerAsync(db, userId, lastSeen: null, ct);
            const int newMatches = 53;
            for (var i = 0; i < newMatches; i++)
            {
                var ad = await SeedJobAdAsync(
                    db, ClockAt(T0), $"Annons {i}", $"Company {i}", $"https://example.com/{i}", ct);
                await SeedMatchAsync(db, userId, ad, NotifiableMatchGrade.Good, T0.AddDays(i), ct);
            }

            var countHandler = new GetMyNewMatchCountQueryHandler(db, UserWith(userId));
            var listHandler = new GetMyMatchesQueryHandler(db, UserWith(userId));

            var count = await countHandler.Handle(new GetMyNewMatchCountQuery(), ct);
            var list = await listHandler.Handle(new GetMyMatchesQuery(), ct);

            // The badge is the UNCAPPED true total — all 53 new matches counted (ADR 0080 PR-5
            // verbatim COUNT WHERE CreatedAt > last_seen).
            count.Count.ShouldBe(newMatches,
                "GetMyNewMatchCount is the true new-match cardinality — uncapped");

            // The list is a bounded recent-VIEW — capped at 50; with a null watermark every visible
            // row is new, so visible-new = min(newCount, 50) = 50.
            list.Count.ShouldBe(50, "GetMyMatches caps the recent-view at 50 rows");
            list.Count(r => r.IsNew).ShouldBe(50,
                "with a null watermark every visible row is new → visible-new = min(newCount, 50)");

            // THE DIVERGENCE the #273 contract documents and intentionally permits: badge > list.
            // Do NOT clamp the count to the cap — the remainder is reachable via /jobb grade-filter.
            count.Count.ShouldBeGreaterThan(list.Count,
                "for a heavy user the uncapped badge (53) intentionally exceeds the 50-capped list");
        }
    }

    [Fact]
    public async Task NewMatchCount_EqualsTheFiftyCappedList_WhenUserHasExactlyFiftyNewMatches()
    {
        // The cap-EQUALITY seam (#273): at exactly 50 new matches the count and the list coincide —
        // divergence is precisely zero (50 is NOT > 50). Brackets the transition (50 = equal,
        // 53 = diverge) and pins the > 50 / >= 50 off-by-one a future clamp refactor would trip.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, _, scope) = NewScope();
        using (scope)
        {
            await SeedSeekerAsync(db, userId, lastSeen: null, ct);
            const int newMatches = 50;
            for (var i = 0; i < newMatches; i++)
            {
                var ad = await SeedJobAdAsync(
                    db, ClockAt(T0), $"Annons {i}", $"Company {i}", $"https://example.com/{i}", ct);
                await SeedMatchAsync(db, userId, ad, NotifiableMatchGrade.Good, T0.AddDays(i), ct);
            }

            var count = await new GetMyNewMatchCountQueryHandler(db, UserWith(userId))
                .Handle(new GetMyNewMatchCountQuery(), ct);
            var list = await new GetMyMatchesQueryHandler(db, UserWith(userId))
                .Handle(new GetMyMatchesQuery(), ct);

            count.Count.ShouldBe(newMatches, "exactly 50 new matches → the count is 50");
            list.Count.ShouldBe(50, "the list shows all 50 (at the cap, none dropped)");
            list.Count(r => r.IsNew).ShouldBe(50, "every visible row is new");
            // At the seam the two surfaces coincide — NO divergence yet.
            count.Count.ShouldBe(list.Count, "at exactly the cap the badge and the list are equal");
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
