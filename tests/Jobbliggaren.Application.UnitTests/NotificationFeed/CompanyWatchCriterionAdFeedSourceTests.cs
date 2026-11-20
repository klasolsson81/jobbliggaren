using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #1663 PR 3 (ADR 0175 D8) — the Branschbevakning source's branch logic under EF InMemory: its own switch and the
/// existence short-circuit, the new set as its whole list (F-A), the token, the count of distinct unread ads, one row per
/// ad, the window's horizon and the shortfall, and the ads both higher sources hold under every gate state. The Npgsql
/// translation, the marks (all <c>ExecuteUpdate</c>, which InMemory cannot run) and the endpoints are
/// <c>NotificationFeedCriterionSourceTests</c>'.
/// </summary>
public class CompanyWatchCriterionAdFeedSourceTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private const NotificationFeedSourceKind Kind = NotificationFeedSourceKind.CompanyWatchCriterionAd;
    private readonly NotificationFeedSeed _seed = new();

    // The source grades nothing; the ports are only here because the read context carries them.
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    private Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId, int maxItems = 20) =>
        new CompanyWatchCriterionAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, maxItems, _profileBuilder, _perUserSearch),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReadAsync_WithTheTypeOff_IsNothing_AndAnotherUsersSwitchIsTheirOwn()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        _seed.CriterionHit(db, _seed.Criterion(db, me), _seed.ActiveAd(db), T0.AddHours(1));
        _seed.CriterionHit(db, _seed.Criterion(db, other), _seed.ActiveAd(db), T0.AddHours(1));
        _seed.SwitchFeedCriterionAds(db, mine, on: false, T0.AddHours(2));

        (await ReadAsync(db, me)).ShouldBe(NotificationSourceRead.Nothing(Kind));
        (await ReadAsync(db, other)).UnreadCount.ShouldBe(1);
        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_WithNoBranschbevakning_IsNothing()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        // Another user's criterion and hit are not this user's.
        var other = Guid.NewGuid();
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        _seed.CriterionHit(db, _seed.Criterion(db, other), _seed.ActiveAd(db), T0.AddHours(1));

        (await ReadAsync(db, userId)).ShouldBe(NotificationSourceRead.Nothing(Kind));
    }

    // F-A as a table (ADR 0175 D6): the source lists the new set and nothing else. A hit acknowledged on its criterion's page
    // leaves the bell whether or not it was read there; a new hit is listed, unread exactly when not read in the bell.
    public static TheoryData<bool, bool> EveryCombination()
    {
        var cases = new TheoryData<bool, bool>();
        foreach (var acknowledged in new[] { false, true })
            foreach (var readInBell in new[] { false, true })
                cases.Add(acknowledged, readInBell);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public async Task ReadAsync_OverEveryCombination_ListsAndCountsTheNewSetOnly(bool acknowledged, bool readInBell)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var criterion = _seed.Criterion(db, userId);
        var ad = _seed.ActiveAd(db);
        _seed.CriterionHit(db, criterion, ad, T0.AddHours(2));
        if (readInBell)
            NotificationFeedSeed.CriterionHitsReadInBell(db, userId, ad);
        if (acknowledged)
            NotificationFeedSeed.SeeCriterionNewAdsThrough(db, criterion, T0.AddHours(3));

        var read = await ReadAsync(db, userId);

        if (acknowledged)
        {
            read.ShouldBe(NotificationSourceRead.Nothing(Kind), "an acknowledged hit is no longer in the new set");
            return;
        }

        read.UnreadCount.ShouldBe(readInBell ? 0 : 1);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2), "the token is the newest new hit, read in the bell or not");
        var item = read.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(Kind);
        item.JobAdId.ShouldBe(ad.Value);
        item.IsUnread.ShouldBe(!readInBell);
        item.Grade.ShouldBeNull();
        read.Horizon.ShouldBeNull();
    }

    [Theory]
    [InlineData(CompanyWatchCriterionScanOutcome.TooBroad)]
    [InlineData(CompanyWatchCriterionScanOutcome.NotMaterialised)]
    [InlineData(null)]
    public async Task ReadAsync_ACriterionWhoseLastScanDidNotAnswerScanned_ContributesNothing(
        CompanyWatchCriterionScanOutcome? outcome)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        _seed.CriterionHit(db, _seed.Criterion(db, userId, outcome), _seed.ActiveAd(db), T0.AddHours(2));

        (await ReadAsync(db, userId)).ShouldBe(NotificationSourceRead.Nothing(Kind));
    }

    [Fact]
    public async Task ReadAsync_AnAdTwoCriteriaReach_IsOneRowCountedOnce_AndUnreadWhileAnyOfItsHitsIsUnread()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var first = _seed.Criterion(db, userId);
        var second = _seed.Criterion(db, userId);
        var ad = _seed.ActiveAd(db);
        _seed.CriterionHit(db, first, ad, T0.AddHours(1));
        _seed.CriterionHit(db, second, ad, T0.AddHours(2));

        var both = await ReadAsync(db, userId);
        both.UnreadCount.ShouldBe(1);
        var row = both.Items.ShouldHaveSingleItem();
        row.OccurredAt.ShouldBe(T0.AddHours(2), "the ad's moment is its newest hit");
        row.IsUnread.ShouldBeTrue();

        // The newer hit read (the switch-on backlog's state), the older one not: the ad is still unread.
        NotificationFeedSeed.CriterionHitReadInBell(db, second, ad);
        var oneRead = await ReadAsync(db, userId);
        oneRead.UnreadCount.ShouldBe(1);
        oneRead.Items.ShouldHaveSingleItem().IsUnread.ShouldBeTrue();

        NotificationFeedSeed.CriterionHitsReadInBell(db, userId, ad);
        var allRead = await ReadAsync(db, userId);
        allRead.UnreadCount.ShouldBe(0);
        allRead.Items.ShouldHaveSingleItem().IsUnread.ShouldBeFalse();
    }

    [Fact]
    public async Task ReadAsync_ADismissedAd_LeavesTheListButKeepsTheToken()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var criterion = _seed.Criterion(db, userId);
        var kept = _seed.ActiveAd(db);
        var dismissed = _seed.ActiveAd(db);
        _seed.CriterionHit(db, criterion, kept, T0.AddHours(1));
        _seed.CriterionHit(db, criterion, dismissed, T0.AddHours(2));
        NotificationFeedSeed.CriterionHitsDismissed(db, userId, dismissed);

        var read = await ReadAsync(db, userId);

        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.UnreadCount.ShouldBe(1);
        read.Items.ShouldHaveSingleItem().JobAdId.ShouldBe(kept.Value);
    }

    // ── the ads a higher source holds (ADR 0175 D8) ─────────────────────────────────────────

    public enum MatchRowState { None, Unread, Read, Dismissed, BehindTheWatermark }

    public enum MatchGate { On, ConsentWithdrawn, SwitchedOff }

    public static TheoryData<MatchRowState, MatchGate> EveryMatchCombination()
    {
        var cases = new TheoryData<MatchRowState, MatchGate>();
        foreach (var state in Enum.GetValues<MatchRowState>())
            foreach (var gate in Enum.GetValues<MatchGate>())
                cases.Add(state, gate);
        return cases;
    }

    // An ad is held by the match source exactly when the user has a match row for it, whatever the row's state, AND the
    // match gate holds. A held ad is no Branschbevakning notification; the token still covers its hit.
    [Theory]
    [MemberData(nameof(EveryMatchCombination))]
    public async Task ReadAsync_LeavesOutExactlyTheAdsTheMatchSourceHolds(MatchRowState state, MatchGate gate)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.CriterionHit(db, _seed.Criterion(db, userId), ad, T0.AddHours(2));
        if (state != MatchRowState.None)
            _seed.Match(db, userId, ad, NotifiableMatchGrade.Strong, T0.AddHours(1));
        if (state == MatchRowState.Read)
            NotificationFeedSeed.MatchReadInBell(db, userId, ad);
        if (state == MatchRowState.Dismissed)
            MatchDismissedInBell(db, userId, ad);
        if (state == MatchRowState.BehindTheWatermark)
            _seed.SeeMatchesThrough(db, seeker, T0.AddHours(1).AddMinutes(30));
        if (gate == MatchGate.ConsentWithdrawn)
            _seed.SetConsent(db, seeker, enabled: false, T0.AddHours(3));
        if (gate == MatchGate.SwitchedOff)
            _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(3));

        var read = await ReadAsync(db, userId);

        var held = state != MatchRowState.None && gate == MatchGate.On;
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2), "the token covers a held hit");
        read.UnreadCount.ShouldBe(held ? 0 : 1);
        read.Items.Select(i => i.JobAdId).ShouldBe(held ? Array.Empty<Guid>() : new[] { ad.Value });
    }

    public enum FollowHitState { None, Unread, Read, Dismissed, BehindTheWatermark, GradedOut, UnderAnUnfollowedWatch }

    public enum FollowGate { On, SwitchedOff }

    public static TheoryData<FollowHitState, FollowGate> EveryFollowCombination()
    {
        var cases = new TheoryData<FollowHitState, FollowGate>();
        foreach (var state in Enum.GetValues<FollowHitState>())
            foreach (var gate in Enum.GetValues<FollowGate>())
                cases.Add(state, gate);
        return cases;
    }

    // An ad is held by the follow source exactly when the user has a hit for it under an active watch, whatever the hit's
    // state (an "endast matchade" watch's ad the follow source grades out included: in the bell through neither source,
    // decided in ADR 0175 D8), AND the follow switch is on. An unfollowed watch holds nothing.
    [Theory]
    [MemberData(nameof(EveryFollowCombination))]
    public async Task ReadAsync_LeavesOutExactlyTheAdsTheFollowSourceHolds(FollowHitState state, FollowGate gate)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var ad = _seed.ActiveAd(db);
        _seed.CriterionHit(db, _seed.Criterion(db, userId), ad, T0.AddHours(2));
        if (state != FollowHitState.None)
        {
            var watch = _seed.Watch(db, userId, onlyMatched: state == FollowHitState.GradedOut);
            _seed.Hit(db, userId, ad, watch, T0.AddHours(1));
            if (state == FollowHitState.Read)
                NotificationFeedSeed.HitsReadInBell(db, userId, ad);
            if (state == FollowHitState.Dismissed)
                NotificationFeedSeed.HitsDismissed(db, userId, ad);
            if (state == FollowHitState.BehindTheWatermark)
                _seed.SeeFollowedAdsThrough(db, seeker, T0.AddHours(1).AddMinutes(30));
            if (state == FollowHitState.UnderAnUnfollowedWatch)
                _seed.Unfollow(db, watch);
        }

        if (gate == FollowGate.SwitchedOff)
            _seed.SwitchFeedFollowedAds(db, seeker, on: false, T0.AddHours(3));

        var read = await ReadAsync(db, userId);

        var held = state is not (FollowHitState.None or FollowHitState.UnderAnUnfollowedWatch) && gate == FollowGate.On;
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2), "the token covers a held hit");
        read.UnreadCount.ShouldBe(held ? 0 : 1);
        read.Items.Select(i => i.JobAdId).ShouldBe(held ? Array.Empty<Guid>() : new[] { ad.Value });
        await _perUserSearch.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>());
    }

    // ── the window, the horizon and the shortfall ─────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_WhenTheWindowIsFull_SetsTheHorizonAtTheHitJustPastIt()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var criterion = _seed.Criterion(db, userId);
        const int maxItems = 2;
        var windowHits = maxItems * CompanyWatchCriterionAdFeedSource.HitsPerRow;
        for (var i = 1; i <= windowHits + 1; i++)
        {
            var ad = _seed.ActiveAd(db);
            _seed.CriterionHit(db, criterion, ad, T0.AddHours(i));
            NotificationFeedSeed.CriterionHitsReadInBell(db, userId, ad);
        }

        var read = await ReadAsync(db, userId, maxItems);

        read.UnreadCount.ShouldBe(0);
        read.Items.Count.ShouldBe(windowHits);
        read.Horizon.ShouldBe(T0.AddHours(1), "the oldest hit is the one just past the window");
    }

    [Fact]
    public async Task ReadAsync_WhenReadRowsCrowdAnUnreadOneOutOfTheWindow_NamesItAsUnread()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var criterion = _seed.Criterion(db, userId);
        var unread = _seed.ActiveAd(db);
        _seed.CriterionHit(db, criterion, unread, T0.AddHours(1));
        const int maxItems = 1;
        for (var i = 0; i < maxItems * CompanyWatchCriterionAdFeedSource.HitsPerRow; i++)
        {
            var read = _seed.ActiveAd(db);
            _seed.CriterionHit(db, criterion, read, T0.AddHours(10 + i));
            NotificationFeedSeed.CriterionHitsReadInBell(db, userId, read);
        }

        var result = await ReadAsync(db, userId, maxItems);

        result.UnreadCount.ShouldBe(1);
        var named = result.Items.Where(i => i.IsUnread).ShouldHaveSingleItem();
        named.JobAdId.ShouldBe(unread.Value);
        named.OccurredAt.ShouldBe(T0.AddHours(1));
    }

    /// <summary>The user's X on a match, as <c>MatchFeedSource</c>'s one statement leaves it; InMemory cannot run it.</summary>
    private static void MatchDismissedInBell(AppDbContext db, Guid userId, JobAdId adId)
    {
        var match = db.UserJobAdMatches.Single(m => m.UserId == userId && m.JobAdId == adId);
        db.Entry(match).Property(m => m.ReadInNotificationFeed).CurrentValue = true;
        db.Entry(match).Property(m => m.DismissedFromNotificationFeed).CurrentValue = true;
        db.SaveChanges();
    }
}
