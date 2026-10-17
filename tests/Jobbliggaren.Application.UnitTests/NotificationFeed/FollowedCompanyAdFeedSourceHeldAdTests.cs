using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #2158 — the follow source leaves out every ad the match source holds while match notifications reach the bell: an ad
/// the user has any match row for, whatever that row's state, is no follow notification, and the follow source speaks for
/// it again as soon as the match gate is off. Branch logic under EF InMemory; the Npgsql translation and the endpoints are
/// <c>NotificationFeedOneNotificationPerAdTests</c>'s.
/// </summary>
public class FollowedCompanyAdFeedSourceHeldAdTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    public FollowedCompanyAdFeedSourceHeldAdTests()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlySet<JobAdId>)call.ArgAt<IReadOnlyCollection<JobAdId>>(1).ToHashSet());
    }

    public enum MatchRowState { None, Unread, Read, Dismissed, BehindTheWatermark }

    public enum MatchGate { On, ConsentWithdrawn, SwitchedOff }

    private Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId, int maxItems = 20) =>
        new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, maxItems, _profileBuilder, _perUserSearch),
            TestContext.Current.CancellationToken);

    /// <summary>The user's X on a match (#2151), as <c>MatchFeedSource</c>'s one statement leaves it; InMemory cannot run it.</summary>
    private static void MatchDismissedInBell(AppDbContext db, Guid userId, JobAdId adId)
    {
        var match = db.UserJobAdMatches.Single(m => m.UserId == userId && m.JobAdId == adId);
        db.Entry(match).Property(m => m.ReadInNotificationFeed).CurrentValue = true;
        db.Entry(match).Property(m => m.DismissedFromNotificationFeed).CurrentValue = true;
        db.SaveChanges();
    }

    // The rule as a table: an ad is held, and so no follow notification, exactly when a match row of the user's exists for
    // it AND the match gate holds; the row's own state never matters. Both paths: common, and the grade path of an
    // "endast matchade" watch.
    public static TheoryData<MatchRowState, MatchGate, bool> EveryCombination()
    {
        var cases = new TheoryData<MatchRowState, MatchGate, bool>();
        foreach (var match in Enum.GetValues<MatchRowState>())
            foreach (var gate in Enum.GetValues<MatchGate>())
                foreach (var onlyMatched in new[] { false, true })
                    cases.Add(match, gate, onlyMatched);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public async Task ReadAsync_LeavesOutAnAdTheMatchSourceHolds_ExactlyWhileTheMatchGateHolds(
        MatchRowState match, MatchGate gate, bool onlyMatched)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var watch = _seed.Watch(db, userId, onlyMatched);
        var ad = _seed.ActiveAd(db);
        if (match != MatchRowState.None)
        {
            _seed.Match(db, userId, ad, NotifiableMatchGrade.Strong, T0.AddHours(1));
            if (match == MatchRowState.Read)
                NotificationFeedSeed.MatchReadInBell(db, userId, ad);
            if (match == MatchRowState.Dismissed)
                MatchDismissedInBell(db, userId, ad);
            if (match == MatchRowState.BehindTheWatermark)
                _seed.SeeMatchesThrough(db, seeker, T0.AddHours(2));
        }

        _seed.Hit(db, userId, ad, watch, T0.AddHours(3));
        if (gate == MatchGate.ConsentWithdrawn)
            _seed.SetConsent(db, seeker, enabled: false, T0.AddHours(4));
        if (gate == MatchGate.SwitchedOff)
            _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(4));

        var read = await ReadAsync(db, userId);

        var held = match != MatchRowState.None && gate == MatchGate.On;
        Guid[] listed = held ? [] : [ad.Value];
        read.UnreadCount.ShouldBe(held ? 0 : 1);
        read.Items.Select(i => i.JobAdId).ShouldBe(listed);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(3), "the token is the newest new hit, held or not");
        if (onlyMatched)
            _perUserSearch.ReceivedCalls()
                .Count(call => call.GetMethodInfo().Name == nameof(IPerUserJobAdSearchQuery.FilterToMatchingAsync))
                .ShouldBe(held ? 0 : 1, "a held ad is never graded");
    }

    [Fact]
    public async Task ReadAsync_AnotherUsersMatchRowHoldsNothing()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        _seed.Seeker(db, me, backgroundMatchConsent: true);
        _seed.Seeker(db, other, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, other, ad, NotifiableMatchGrade.Top, T0.AddHours(1));
        _seed.Hit(db, me, ad, _seed.Watch(db, me, onlyMatched: false), T0.AddHours(2));

        var read = await ReadAsync(db, me);

        read.UnreadCount.ShouldBe(1);
        read.Items.Single().JobAdId.ShouldBe(ad.Value);
    }

    // Seven held ads carry the newest hits; with a window of six (two rows) a read ad and an unread one behind them are
    // still the window's, and no horizon is set: a held hit takes no slot.
    [Fact]
    public async Task ReadAsync_HeldHitsTakeNoWindowSlot_AndMoveNoHorizon()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        for (var n = 1; n <= 7; n++)
        {
            var held = _seed.ActiveAd(db, $"Hållen {n}");
            _seed.Match(db, userId, held, NotifiableMatchGrade.Strong, T0.AddHours(10 + n));
            _seed.Hit(db, userId, held, watch, T0.AddHours(10 + n));
        }

        var read = _seed.ActiveAd(db, "Läst");
        _seed.Hit(db, userId, read, watch, T0.AddHours(5));
        NotificationFeedSeed.HitsReadInBell(db, userId, read);
        var unread = _seed.ActiveAd(db, "Oläst");
        _seed.Hit(db, userId, unread, watch, T0.AddHours(4));

        var feed = await ReadAsync(db, userId, maxItems: 2);

        feed.Items.Select(i => (i.JobAdId, i.IsUnread)).ShouldBe([(read.Value, false), (unread.Value, true)]);
        feed.Horizon.ShouldBeNull();
        feed.UnreadCount.ShouldBe(1);
        feed.AcknowledgedThrough.ShouldBe(T0.AddHours(17));
    }

    // The whole bell: an ad that is both a match and a followed company's new ad is one notification, the match row, and
    // the badge counts it once.
    [Fact]
    public async Task Handle_AnAdThatIsBothAMatchAndAFollowedAd_IsOneNotification_TheMatchRow()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var both = _seed.ActiveAd(db, "Båda");
        var followedOnly = _seed.ActiveAd(db, "Bara följd");
        _seed.Match(db, userId, both, NotifiableMatchGrade.Top, T0.AddHours(1));
        _seed.Hit(db, userId, both, watch, T0.AddHours(2));
        _seed.Hit(db, userId, followedOnly, watch, T0.AddHours(3));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var feed = await new GetMyNotificationFeedQueryHandler(db, currentUser, _profileBuilder, _perUserSearch)
            .Handle(new GetMyNotificationFeedQuery(), TestContext.Current.CancellationToken);

        feed.Items.Select(i => (i.Kind, i.JobAdId)).ShouldBe(
            [(NotificationFeedSourceKind.FollowedCompanyAd, followedOnly.Value), (NotificationFeedSourceKind.Match, both.Value)]);
        feed.Sources.Sum(s => s.UnreadCount).ShouldBe(2);
    }
}
