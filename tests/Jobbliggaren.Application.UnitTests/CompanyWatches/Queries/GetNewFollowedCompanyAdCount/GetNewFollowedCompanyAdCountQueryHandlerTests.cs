using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B / RF-8=8C) — the Översikt follow-rail count handler. Mirrors the
/// EF-InMemory <see cref="TestAppDbContextFactory"/> pattern + NSubstitute ports. This project asserts
/// the BRANCH logic — the READ-TIME GRADE FILTER (8C), the profile-less INERT fork, the
/// no-OnlyMatched common path, and owner-scope — while the Testcontainers sibling
/// (<c>FollowedCompanyAdRailTests</c>) is the real-DB oracle for the value-converted hit↔watch JOIN
/// translation, the #864 lifecycle gate's translation (the <c>JobAds</c> join + SmartEnum
/// <c>Status</c> comparison), and the watermark boundary (InMemory can drift on DateTimeOffset
/// comparison, so the watermark tests all use a NULL watermark = every hit new).
/// <list type="bullet">
/// <item>no authenticated user / no active follows → honest 0 (grade ports never touched);</item>
/// <item>no OnlyMatched watch (the common path) → all hits count, <c>FilterToMatchingAsync</c> is
///   NEVER called;</item>
/// <item>OnlyMatched + assessable profile → only the ≥Good hits count (the rest are excluded, never
///   phantom-shown);</item>
/// <item>OnlyMatched + profile-less → INERT: every hit counts, the fail-fast port is never called.</item>
/// </list>
/// </summary>
public class GetNewFollowedCompanyAdCountQueryHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly IDateTimeProvider _clock = new FixedClock(T0);
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    // A fixed-time clock (the aggregate stamps are relative to T0; the handler reads no clock).
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    // An ASSESSABLE profile: non-empty Fast.SsykGroupConceptIds → FilterToMatchingAsync IS consulted.
    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    // A PROFILE-LESS profile: empty Fast.SsykGroupConceptIds → the "endast matchade" filter is INERT.
    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    private GetNewFollowedCompanyAdCountQueryHandler Sut(AppDbContext db, ICurrentUser user) =>
        new(db, user, _profileBuilder, _perUserSearch);

    private void SeedSeeker(AppDbContext db, Guid userId) =>
        db.JobSeekers.Add(JobSeeker.Register(userId, "Test User", _clock).Value); // null watermark

    private CompanyWatchId SeedWatch(AppDbContext db, Guid userId, bool onlyMatched, bool active = true)
    {
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        if (!active)
            watch.SoftDelete(_clock);
        db.CompanyWatches.Add(watch);
        return watch.Id;
    }

    // Seeds an ACTIVE JobAd and a hit pointing at it.
    //
    // #864 — this used to be a bare `JobAdId.New()` with NO JobAd row ever inserted, so the whole suite
    // proved a count over ads that DO NOT EXIST. That was invisible while the handler joined nothing —
    // and "the handler joins nothing" IS the defect: the rail counted hit rows while its destination
    // (/foretag, ListCompanyWatchesQueryHandler:100) has always been Status == Active gated, so the badge
    // could promise ads the page would never show. The rail is now lifecycle-gated, so a hit's ad must be
    // real. Every test's own axis (watermark / owner-scope / OnlyMatched / inert-filter) is untouched.
    private JobAdId SeedHit(AppDbContext db, Guid userId, CompanyWatchId watchId)
    {
        var adId = SeedActiveAd(db);
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, adId, watchId, _clock).Value);
        return adId;
    }

    private JobAdId SeedActiveAd(AppDbContext db)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var import = JobAd.Import(
            title: "Roll",
            company: Company.Create("Bolag AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(60),
            clock: _clock);
        import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");
        db.JobAds.Add(import.Value);
        db.SaveChanges();
        return import.Value.Id;
    }

    private async Task<int> CountAsync(AppDbContext db, Guid userId)
    {
        var result = await Sut(db, UserWith(userId)).Handle(
            new GetNewFollowedCompanyAdCountQuery(), TestContext.Current.CancellationToken);
        return result.Count;
    }

    // ═══ #864 — the rail counts what /foretag can SHOW.
    //
    // This handler joined JobAds NOT AT ALL: it counted hit rows. Its destination has always been
    // Status == Active gated (ListCompanyWatchesQueryHandler:100), so the badge said "3 nya annonser
    // från företag du bevakar", the user clicked, and /foretag showed zero — a count that promises more
    // than its set can deliver. CompanyWatchScanJob's own Active gate (:156) only proves the ad was live
    // when the HIT was recorded; archiving is every ad's normal end of life.
    //
    // Both branches are covered: the common path COUNTs the gated query in SQL, the grade path
    // materialises it. A single gate at the source serves both.
    //
    // SEEDS ARE ASYMMETRIC (more live than archived), deliberately. A count-only DTO cannot say WHICH
    // rows it counted, so a 1-live+1-archived seed passes under the INVERTED gate too (== Archived also
    // counts exactly 1). Asymmetry separates every state: gate correct / gate deleted / gate inverted
    // all read different counts.
    [Fact]
    public async Task Handle_CommonPath_DoesNotCountHit_WhenItsAdIsArchived()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, userId);
        var watch = SeedWatch(db, userId, onlyMatched: false); // → common path, pure SQL COUNT
        SeedHit(db, userId, watch);
        SeedHit(db, userId, watch);
        var archivedAdId = SeedHit(db, userId, watch);
        await db.SaveChangesAsync(ct);

        // Archived through the domain transition production performs (ExpireJobAdsJob) — never a
        // fabricated column value (#843 / #864 AC 4).
        db.JobAds.Single(j => j.Id == archivedAdId).Archive(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        // 2, not 3 (gate deleted) and not 1 (gate inverted). NON-VACUOUS BY CONSTRUCTION: the live
        // hits must still be counted, so a gate that excluded everything would fail this too.
        (await CountAsync(db, userId)).ShouldBe(2,
            "rälen får inte räkna en annons /foretag inte visar — badgen och destinationen måste " +
            "räkna samma presenterbara mängd, annars säger den '3 nya' och sidan visar 2");
    }

    [Fact]
    public async Task Handle_GradePath_DoesNotCountHit_WhenItsAdIsArchived()
    {
        // MIXED watches, and the archived-on-a-PLAIN-watch hit is the load-bearing seed: the port
        // substitute below models the REAL FilterToMatchingAsync (itself Status == Active gated,
        // PerUserJobAdSearchQuery:370), so an archived hit under an OnlyMatched watch is dropped by
        // the port-model whether or not the handler's own gate exists — it cannot observe a deleted
        // gate. The plain watch's archived hit bypasses the port entirely, so it CAN. Without it,
        // "delete the gate" would stay green here — the transitive-gate blindness that hid the
        // digest drain leak (#864 review round), designed out of the spec instead of into it.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();

        SeedSeeker(db, userId);
        var plainWatch = SeedWatch(db, userId, onlyMatched: false);
        var gradedWatch = SeedWatch(db, userId, onlyMatched: true); // → the grade path materialises
        SeedHit(db, userId, plainWatch);                            // live, plain → counts
        var archivedPlainAdId = SeedHit(db, userId, plainWatch);    // archived, plain → the delete-detector
        var liveGradedAdId = SeedHit(db, userId, gradedWatch);      // live, ≥Good → counts
        var archivedGradedAdId = SeedHit(db, userId, gradedWatch);  // archived, graded → gate excludes at source
        await db.SaveChangesAsync(ct);

        db.JobAds.Single(j => j.Id == archivedPlainAdId).Archive(_clock).IsSuccess.ShouldBeTrue();
        db.JobAds.Single(j => j.Id == archivedGradedAdId).Archive(_clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(AssessableProfile());
        // Models the real port faithfully: Active-gated, so it can never return an archived id
        // (#843 / AC 4 — no state production cannot reach). With the handler's own gate in place it
        // is never even OFFERED one — the source gate runs first; that redundancy is deliberate.
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { liveGradedAdId });

        // 2 = live-plain + live-graded. Gate deleted → 3 (the plain archived hit sneaks in). Gate
        // inverted → 1 (only the plain archived hit; the graded one dies in the port-model).
        (await CountAsync(db, userId)).ShouldBe(2,
            "samma grind måste bita på grad-vägen — den materialiserar samma query, och en arkiverad " +
            "annons under en vanlig bevakning får inte smyga in via den ogrindade armen");
    }

    [Fact]
    public async Task Handle_ReturnsZero_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        SeedHit(db, Guid.NewGuid(), SeedWatch(db, Guid.NewGuid(), onlyMatched: false));
        await db.SaveChangesAsync(ct);

        var result = await Sut(db, UserWith(null)).Handle(new GetNewFollowedCompanyAdCountQuery(), ct);

        result.ShouldBe(NewFollowedCompanyAdCountDto.Zero);
    }

    [Fact]
    public async Task Handle_ReturnsZero_WhenNoActiveWatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        await db.SaveChangesAsync(ct);

        (await CountAsync(db, userId)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_CommonPath_CountsAllHits_AndNeverConsultsGradeFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        var watch = SeedWatch(db, userId, onlyMatched: false);
        SeedHit(db, userId, watch);
        SeedHit(db, userId, watch);
        await db.SaveChangesAsync(ct);

        (await CountAsync(db, userId)).ShouldBe(2, "no OnlyMatched watch → every hit counts");

        // The common path never builds a profile or calls the grade filter (hot-path + fail-fast safety).
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyMatched_AssessableProfile_CountsOnlyMatchingHits()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        var watch = SeedWatch(db, userId, onlyMatched: true);
        var adMatching = SeedHit(db, userId, watch);   // ≥Good
        SeedHit(db, userId, watch);                     // below floor → excluded
        await db.SaveChangesAsync(ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(AssessableProfile());
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { adMatching });

        (await CountAsync(db, userId)).ShouldBe(1, "only the ≥Good hit counts under an OnlyMatched watch");
    }

    [Fact]
    public async Task Handle_OnlyMatched_ProfilelessUser_IsInert_CountsAllHits()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        var watch = SeedWatch(db, userId, onlyMatched: true);
        SeedHit(db, userId, watch);
        SeedHit(db, userId, watch);
        await db.SaveChangesAsync(ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(ProfilelessProfile());

        (await CountAsync(db, userId)).ShouldBe(2,
            "a profile-less user's OnlyMatched filter is INERT — every hit counts (never a dishonest 0)");

        // The fail-fast port is NEVER called for a profile-less user (it throws on an empty-SSYK profile).
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Mixed_PlainWatchHitsAlwaysCount_OnlyMatchedFiltered()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        var plainWatch = SeedWatch(db, userId, onlyMatched: false);
        var gradedWatch = SeedWatch(db, userId, onlyMatched: true);
        SeedHit(db, userId, plainWatch);                     // plain → always counts
        var adMatching = SeedHit(db, userId, gradedWatch);   // ≥Good → counts
        SeedHit(db, userId, gradedWatch);                    // below floor → excluded
        await db.SaveChangesAsync(ct);

        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(AssessableProfile());
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId> { adMatching });

        (await CountAsync(db, userId)).ShouldBe(2, "plain-watch hit + the one ≥Good OnlyMatched hit");
    }

    [Fact]
    public async Task Handle_ExcludesUnfollowedWatchHits()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);
        var active = SeedWatch(db, userId, onlyMatched: false);
        var unfollowed = SeedWatch(db, userId, onlyMatched: false, active: false);
        SeedHit(db, userId, active);
        SeedHit(db, userId, unfollowed);
        await db.SaveChangesAsync(ct);

        (await CountAsync(db, userId)).ShouldBe(1, "the unfollowed watch's hit is excluded (present-tense follows)");
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_AnotherUsersHitsNotCounted()
    {
        var ct = TestContext.Current.CancellationToken;
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, me);
        SeedHit(db, me, SeedWatch(db, me, onlyMatched: false));
        SeedHit(db, other, SeedWatch(db, other, onlyMatched: false));
        await db.SaveChangesAsync(ct);

        (await CountAsync(db, me)).ShouldBe(1, "only my own follows' hits count");
    }
}
