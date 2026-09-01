using System.Globalization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
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

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries.ListNewFollowedCompanyAds;

/// <summary>
/// #1576 — the destination the Översikt count links to. Branch logic only: the cap and its
/// acknowledgement window, the OnlyMatched fork on the list side, and the assessed/not-assessed fork.
/// The Testcontainers sibling (<c>FollowedCompanyAdRailTests</c>) remains the oracle for SQL
/// translation and for the count-equals-list property; nothing here proves either, and it must not
/// claim to — <c>NewFollowedCompanyAdSet</c> documents that InMemory hides a shaper failure this
/// suite cannot see.
///
/// <para>
/// The cap specs are the reason this file exists at all. Acknowledging the newest N would swallow
/// everything older permanently (the watermark only moves forward), and cutting INSIDE a group of
/// hits that share a CreatedAt would swallow the ones beyond the cut for the same reason. Both are
/// invisible to a suite that never crosses the cap.
/// </para>
/// </summary>
public class ListNewFollowedCompanyAdsQueryHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = new FixedClock(T0);
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private static FullCandidateMatchProfile AssessableProfile() =>
        new(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []);

    private static FullCandidateMatchProfile ProfilelessProfile() =>
        new(new CandidateMatchProfile("", [], [], [], []), []);

    private ListNewFollowedCompanyAdsQueryHandler Sut(AppDbContext db, ICurrentUser user) =>
        new(db, user, _profileBuilder, _perUserSearch);

    private void SeedSeeker(AppDbContext db, Guid userId) =>
        db.JobSeekers.Add(JobSeeker.Register(userId, "Test User", _clock).Value); // null watermark

    private CompanyWatchId SeedWatch(AppDbContext db, Guid userId, bool onlyMatched)
    {
        var orgNr = "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", CultureInfo.InvariantCulture);
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(orgNr).Value, _clock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        return watch.Id;
    }

    // The ad is ingested STRICTLY BEFORE the scan stamps its hit, matching production: the scan only
    // admits ads with j.CreatedAt > since and stamps at now, so hit.CreatedAt > ad.CreatedAt always.
    private static JobAdId SeedHitAt(
        AppDbContext db, Guid userId, CompanyWatchId watchId, DateTimeOffset hitAt)
    {
        var adId = SeedActiveAd(db, hitAt.AddMinutes(-5));
        db.FollowedCompanyAdHits.Add(
            FollowedCompanyAdHit.Create(userId, adId, watchId, new FixedClock(hitAt)).Value);
        return adId;
    }

    private static JobAdId SeedActiveAd(AppDbContext db, DateTimeOffset ingestedAt)
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
            clock: new FixedClock(ingestedAt), declaredContacts: [], extractTerms: TestKeywordExtraction.None);
        import.IsSuccess.ShouldBeTrue($"seed: JobAd.Import måste lyckas ({import.Error?.Code})");
        db.JobAds.Add(import.Value);
        db.SaveChanges();
        return import.Value.Id;
    }

    private async Task<NewFollowedCompanyAdsDto> ListAsync(AppDbContext db, Guid? userId) =>
        await Sut(db, UserWith(userId)).Handle(
            new ListNewFollowedCompanyAdsQuery(), TestContext.Current.CancellationToken);

    private void ProfileIs(FullCandidateMatchProfile profile) =>
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(profile);

    private void MatchingIs(params JobAdId[] ids) =>
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(),
                Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(ids.ToHashSet());

    // ═══ The two early returns (§7: happy path + the refusals).

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoAuthenticatedUser()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await ListAsync(db, userId: null);

        result.Rows.ShouldBeEmpty();
        result.AcknowledgedThrough.ShouldBeNull("nothing was read, so nothing may be acknowledged");
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenUserHasNoActiveWatches()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await ListAsync(db, userId);

        result.Rows.ShouldBeEmpty();
        result.AcknowledgedThrough.ShouldBeNull();
    }

    // ═══ The cap, and the acknowledgement window it produces.

    [Fact]
    public async Task Handle_ReturnsTheCapAndFlagsTruncated_WhenMoreHitsExistThanTheCap()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        for (var i = 0; i <= ListNewFollowedCompanyAdsQueryHandler.MaxRows; i++)
            SeedHitAt(db, userId, watchId, T0.AddMinutes(i));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // The LIST grades every ad in the window (the count grades only OnlyMatched hits), so a
        // profile is always read. Profile-less keeps these specs on the cap axis alone.
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        result.Rows.Count.ShouldBe(ListNewFollowedCompanyAdsQueryHandler.MaxRows);
        result.Truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_DoesNotFlagTruncated_WhenHitsExactlyFillTheCap()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        for (var i = 0; i < ListNewFollowedCompanyAdsQueryHandler.MaxRows; i++)
            SeedHitAt(db, userId, watchId, T0.AddMinutes(i));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // The LIST grades every ad in the window (the count grades only OnlyMatched hits), so a
        // profile is always read. Profile-less keeps these specs on the cap axis alone.
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        result.Rows.Count.ShouldBe(ListNewFollowedCompanyAdsQueryHandler.MaxRows);
        result.Truncated.ShouldBeFalse("exactly the cap is not more than the cap");
    }

    // THE ONE THAT MATTERS. The window is the OLDEST rows, so acknowledging its max leaves everything
    // newer above the watermark for the next visit. Taking the NEWEST N instead would acknowledge past
    // every older hit, and the watermark only moves forward — the loss would be unrecoverable.
    [Fact]
    public async Task Handle_AcknowledgesTheOldestWindow_SoNewerHitsSurviveTheVisit()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        for (var i = 0; i <= ListNewFollowedCompanyAdsQueryHandler.MaxRows; i++)
            SeedHitAt(db, userId, watchId, T0.AddMinutes(i));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // The LIST grades every ad in the window (the count grades only OnlyMatched hits), so a
        // profile is always read. Profile-less keeps these specs on the cap axis alone.
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        var acknowledged = result.AcknowledgedThrough.ShouldNotBeNull();
        acknowledged.ShouldBe(T0.AddMinutes(ListNewFollowedCompanyAdsQueryHandler.MaxRows - 1));
        acknowledged.ShouldBeLessThan(
            T0.AddMinutes(ListNewFollowedCompanyAdsQueryHandler.MaxRows),
            "the hit beyond the cap stays strictly above the watermark, so it comes back");
    }

    // The window must not be cut inside a group sharing a CreatedAt: the read predicate is strict
    // (> lastSeen), so acknowledging a timestamp a row beyond the cap also carries drops that row for
    // good. The tie group is trimmed out of the window instead.
    [Fact]
    public async Task Handle_TrimsTheBoundaryTieGroup_SoNoHitIsExcludedByItsOwnTimestamp()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        var cap = ListNewFollowedCompanyAdsQueryHandler.MaxRows;
        for (var i = 0; i < cap - 1; i++)
            SeedHitAt(db, userId, watchId, T0.AddMinutes(i));
        // Two hits share the boundary timestamp: one would land inside the cap, one outside.
        var tie = T0.AddMinutes(cap - 1);
        SeedHitAt(db, userId, watchId, tie);
        SeedHitAt(db, userId, watchId, tie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // The LIST grades every ad in the window (the count grades only OnlyMatched hits), so a
        // profile is always read. Profile-less keeps these specs on the cap axis alone.
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        result.Truncated.ShouldBeTrue();
        result.Rows.Count.ShouldBe(cap - 1, "the tie group is trimmed out rather than split");
        result.AcknowledgedThrough.ShouldBe(
            T0.AddMinutes(cap - 2), "so both tied hits stay strictly above the watermark");
    }

    // The degenerate arm: the whole capped window is ONE timestamp and more rows share it. A timestamp
    // watermark cannot both show a partial group and acknowledge it, so this visit acknowledges
    // NOTHING. No progress is the safe direction; silent loss is not.
    [Fact]
    public async Task Handle_AcknowledgesNothing_WhenTheWholeCappedWindowSharesOneTimestamp()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        for (var i = 0; i <= ListNewFollowedCompanyAdsQueryHandler.MaxRows; i++)
            SeedHitAt(db, userId, watchId, T0);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        // The LIST grades every ad in the window (the count grades only OnlyMatched hits), so a
        // profile is always read. Profile-less keeps these specs on the cap axis alone.
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        result.Truncated.ShouldBeTrue();
        result.Rows.Count.ShouldBe(ListNewFollowedCompanyAdsQueryHandler.MaxRows);
        result.AcknowledgedThrough.ShouldBeNull(
            "acknowledging the shared timestamp would drop every row beyond the cap for good");
    }

    // ═══ The assessed / not-assessed fork.

    [Fact]
    public async Task Handle_MarksEveryRowNotAssessed_WhenUserStatedNoOccupation()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var onlyMatched = SeedWatch(db, userId, onlyMatched: true);
        SeedHitAt(db, userId, onlyMatched, T0.AddMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ProfileIs(ProfilelessProfile());

        var result = await ListAsync(db, userId);

        result.MatchingAssessed.ShouldBeFalse();
        result.Rows.ShouldAllBe(r => r.MatchesYou == null, "null is silence, never a fabricated false");
        result.Rows.Count.ShouldBe(1, "the OnlyMatched filter is INERT, never a dishonest empty set");
        // The port fail-fasts on an empty-SSYK profile, so it must not be reached at all.
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SetsMatchesYouPerRow_WhenProfileIsAssessable()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var watchId = SeedWatch(db, userId, onlyMatched: false);
        var matching = SeedHitAt(db, userId, watchId, T0.AddMinutes(1));
        var other = SeedHitAt(db, userId, watchId, T0.AddMinutes(2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ProfileIs(AssessableProfile());
        MatchingIs(matching);

        var result = await ListAsync(db, userId);

        result.MatchingAssessed.ShouldBeTrue();
        result.Rows.Count.ShouldBe(2, "no OnlyMatched watch, so neither ad is excluded");
        result.Rows.Single(r => r.Ad.Id == matching.Value).MatchesYou.ShouldBe(true);
        result.Rows.Single(r => r.Ad.Id == other.Value).MatchesYou.ShouldBe(false);
    }

    [Fact]
    public async Task Handle_ExcludesTheAd_WhenItsOnlyWatchIsOnlyMatchedAndItGradesBelowGood()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var onlyMatched = SeedWatch(db, userId, onlyMatched: true);
        SeedHitAt(db, userId, onlyMatched, T0.AddMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ProfileIs(AssessableProfile());
        MatchingIs(); // nothing grades at least Good

        var result = await ListAsync(db, userId);

        result.Rows.ShouldBeEmpty();
    }

    // The window covers EVERY row read, including rows the grade fork excludes. Acknowledging only the
    // included rows would hold the watermark down behind an excluded older hit and re-show the same
    // included ads on every visit.
    [Fact]
    public async Task Handle_AcknowledgesThroughAnExcludedHit_WhenTheNewestRowIsGradeFiltered()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        SeedSeeker(db, userId);
        var plain = SeedWatch(db, userId, onlyMatched: false);
        var onlyMatched = SeedWatch(db, userId, onlyMatched: true);
        var kept = SeedHitAt(db, userId, plain, T0.AddMinutes(1));
        SeedHitAt(db, userId, onlyMatched, T0.AddMinutes(2)); // newest, and excluded below
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        ProfileIs(AssessableProfile());
        MatchingIs(kept);

        var result = await ListAsync(db, userId);

        result.Rows.Count.ShouldBe(1);
        result.Rows[0].Ad.Id.ShouldBe(kept.Value);
        result.AcknowledgedThrough.ShouldBe(
            T0.AddMinutes(2), "the excluded hit was still READ, so the window covers it");
    }
}
