using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #1666 — the bell's followed-company source. Branch logic under EF InMemory: one row per ad, the
/// inclusion rule on displayed rows, the token over every new hit (#1576), one grading call per read,
/// the count refusal past the bound (ADR 0120 clause 5), the window horizon, and parity with
/// Översikt's count. The Npgsql translation is <c>NotificationFeedEndpointTests</c>'s.
/// </summary>
public class FollowedCompanyAdFeedSourceTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    public FollowedCompanyAdFeedSourceTests() =>
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));

    private void GradeAsMatching(params JobAdId[] matching) =>
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>(matching));

    private NotificationFeedReadContext Context(AppDbContext db, Guid userId) =>
        new(db, userId, MaxItems: 20, _profileBuilder, _perUserSearch);

    private Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId, int? bound = null) =>
        new FollowedCompanyAdFeedSource().ReadBoundedAsync(
            Context(db, userId), bound ?? FollowedCompanyAdFeedSource.MaxCountedHits,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReadAsync_WithoutActiveFollows_SaysNothing()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);

        var read = await ReadAsync(db, userId);

        read.ShouldBe(NotificationSourceRead.Nothing(NotificationFeedSourceKind.FollowedCompanyAd));
    }

    // Not consent-gated: follow hits are produced for every active follow; their consent is e-mail's.
    [Fact]
    public async Task ReadAsync_ListsANewAd_WithoutAnyNotificationConsent()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db, "Utvecklare", "Följt AB");
        _seed.Hit(db, userId, ad, watch, T0.AddHours(1));

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(1));
        var item = read.Items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(NotificationFeedSourceKind.FollowedCompanyAd);
        item.JobAdId.ShouldBe(ad.Value);
        item.Title.ShouldBe("Utvecklare");
        item.CompanyName.ShouldBe("Följt AB");
        item.Grade.ShouldBeNull();
        item.IsUnread.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadAsync_AnAdReachedThroughTwoFollows_IsOneRow_AtItsNewestHit()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var direct = _seed.Watch(db, userId, onlyMatched: false);
        var brand = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, direct, T0.AddHours(1));
        _seed.Hit(db, userId, ad, brand, T0.AddHours(2));

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        read.Items.ShouldHaveSingleItem().OccurredAt.ShouldBe(T0.AddHours(2));
    }

    // An "endast matchade" watch's ad below Good is no notification — neither a row nor a count —
    // but its hit still moves the token, so acknowledging cannot be held down behind it (#1576).
    [Fact]
    public async Task ReadAsync_DropsAnOnlyMatchedAdBelowGood_ButItsHitStillSetsTheToken()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var plain = _seed.Watch(db, userId, onlyMatched: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var plainAd = _seed.ActiveAd(db);
        var gradedAd = _seed.ActiveAd(db);
        var belowGoodAd = _seed.ActiveAd(db);
        _seed.Hit(db, userId, plainAd, plain, T0.AddHours(1));
        _seed.Hit(db, userId, gradedAd, onlyMatched, T0.AddHours(2));
        _seed.Hit(db, userId, belowGoodAd, onlyMatched, T0.AddHours(3));
        GradeAsMatching(gradedAd);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(2);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(3));
        read.Items.Select(i => i.JobAdId).ShouldBe([gradedAd.Value, plainAd.Value]);
    }

    // The ad has a new hit through an "endast matchade" watch (below Good) and an old, acknowledged
    // hit through a plain watch: it was already shown, and nothing new and included arrived.
    [Fact]
    public async Task ReadAsync_AnAdWhoseOnlyNewHitIsExcluded_IsNotUnread()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var plain = _seed.Watch(db, userId, onlyMatched: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, plain, T0.AddHours(1));
        _seed.SeeFollowedAdsThrough(db, seeker, T0.AddHours(1));
        _seed.Hit(db, userId, ad, onlyMatched, T0.AddHours(2));
        GradeAsMatching();

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(0);
        var item = read.Items.ShouldHaveSingleItem();
        item.IsUnread.ShouldBeFalse();
        item.OccurredAt.ShouldBe(T0.AddHours(1));
    }

    [Fact]
    public async Task ReadAsync_GradesCountedAndDisplayedAdsInOneCall()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var first = _seed.ActiveAd(db);
        var second = _seed.ActiveAd(db);
        _seed.Hit(db, userId, first, onlyMatched, T0.AddHours(1));
        _seed.Hit(db, userId, second, onlyMatched, T0.AddHours(2));
        GradeAsMatching(first, second);

        await ReadAsync(db, userId);

        await _profileBuilder.Received(1).BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_WithNoOnlyMatchedWatch_NeverGrades()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var plain = _seed.Watch(db, userId, onlyMatched: false);
        _seed.Hit(db, userId, _seed.ActiveAd(db), plain, T0.AddHours(1));

        await ReadAsync(db, userId);

        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
    }

    // ADR 0120 clause 5: past the bound the grade path would count over a truncated read, so the
    // source refuses to count. Never a floor, never a number. The rows still render.
    [Fact]
    public async Task ReadAsync_PastTheCountBound_RefusesTheCount_ButStillListsRows()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var ads = Enumerable.Range(1, 3).Select(_ => _seed.ActiveAd(db)).ToArray();
        for (var i = 0; i < ads.Length; i++)
            _seed.Hit(db, userId, ads[i], onlyMatched, T0.AddHours(i + 1));
        GradeAsMatching(ads);

        var refused = await ReadAsync(db, userId, bound: 2);
        refused.UnreadCount.ShouldBeNull();
        refused.AcknowledgedThrough.ShouldBe(T0.AddHours(3));
        refused.Items.Count.ShouldBe(3);

        var atBound = await ReadAsync(db, userId, bound: 3);
        atBound.UnreadCount.ShouldBe(3);
    }

    [Fact]
    public async Task ReadAsync_WhenTheWindowIsFull_ReportsTheFirstHitPastItAsTheHorizon()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var plain = _seed.Watch(db, userId, onlyMatched: false);
        var window = 20 * FollowedCompanyAdFeedSource.HitsPerRow;
        var hits = window + 2;
        for (var i = 1; i <= hits; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db), plain, T0.AddMinutes(i));

        var read = await ReadAsync(db, userId);

        // Newest first: the window holds minutes hits..3, so the first hit past it is minute 2.
        read.Horizon.ShouldBe(T0.AddMinutes(2));
        read.Items.Count.ShouldBe(window);
        read.UnreadCount.ShouldBe(hits);
    }

    // #2130 (architect finding 2): the window is the newest hits, and an "endast matchade" watch's
    // excluded hits can fill it, so ads the count included were never listed. The grade path lists them
    // from the counted hits, which are already graded, so the badge never counts rows the panel cannot show.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_WhenTheWindowsNewestHitsAreAllExcluded_StillListsTheNewestIncludedUnreadAds()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var older = Enumerable.Range(1, 3).Select(i => _seed.ActiveAd(db, $"Äldre {i}", "Följt AB")).ToArray();
        for (var i = 0; i < older.Length; i++)
            _seed.Hit(db, userId, older[i], onlyMatched, T0.AddMinutes(i + 1));
        var window = 20 * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 1; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db, $"Utesluten {i}", "Annat AB"), onlyMatched, T0.AddHours(1).AddMinutes(i));
        GradeAsMatching(older);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(3);
        read.Items.ShouldAllBe(i => i.IsUnread);
        read.Items.Select(i => i.Title).ShouldBe(["Äldre 3", "Äldre 2", "Äldre 1"]);
        read.Items.ShouldAllBe(i => i.CompanyName == "Följt AB");
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(1).AddMinutes(window + 1));
    }

    [Fact]
    public async Task ReadAsync_OnTheGradePath_ListsMinOfCountAndMaxItemsUnreadRows()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var older = Enumerable.Range(1, 25).Select(i => _seed.ActiveAd(db, $"Äldre {i}", "Följt AB")).ToArray();
        for (var i = 0; i < older.Length; i++)
            _seed.Hit(db, userId, older[i], onlyMatched, T0.AddMinutes(i + 1));
        var window = 20 * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 1; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db, $"Utesluten {i}", "Annat AB"), onlyMatched, T0.AddHours(1).AddMinutes(i));
        GradeAsMatching(older);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(25);
        read.Items.Count(i => i.IsUnread).ShouldBe(20);
        read.Items.Select(i => i.Title).ShouldBe(Enumerable.Range(6, 20).Reverse().Select(n => $"Äldre {n}"));
    }

    // The window and the counted hits overlap on the newest included ads; the ones the window already
    // holds must not be listed a second time when the older ones are added.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_NeverListsAnAdTwice_WhenTheWindowAlreadyHoldsSomeOfThem()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var older = Enumerable.Range(1, 3).Select(i => _seed.ActiveAd(db, $"Äldre {i}", "Följt AB")).ToArray();
        for (var i = 0; i < older.Length; i++)
            _seed.Hit(db, userId, older[i], onlyMatched, T0.AddMinutes(i + 1));
        var window = 20 * FollowedCompanyAdFeedSource.HitsPerRow;
        for (var i = 1; i <= window + 1; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db, $"Utesluten {i}", "Annat AB"), onlyMatched, T0.AddHours(1).AddMinutes(i));
        var newest = Enumerable.Range(1, 2).Select(i => _seed.ActiveAd(db, $"Nyast {i}", "Följt AB")).ToArray();
        for (var i = 0; i < newest.Length; i++)
            _seed.Hit(db, userId, newest[i], onlyMatched, T0.AddHours(3).AddMinutes(i));
        GradeAsMatching([.. older, .. newest]);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(5);
        read.Items.Select(i => i.JobAdId).Distinct().Count().ShouldBe(read.Items.Count);
        read.Items.Count(i => i.IsUnread).ShouldBe(5);
        read.Items.Select(i => i.Title).ShouldBe(["Nyast 2", "Nyast 1", "Äldre 3", "Äldre 2", "Äldre 1"]);
    }

    [Fact]
    // Every excluded row is OLDER than this user's token, so the token bound does not hide it from the
    // list: only the list's own gates do (lifecycle, the active-watch join, the owner filter).
    public async Task ReadAsync_SkipsAnArchivedAd_AnUnfollowedWatch_AndAnotherUsersHits()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        var mine = _seed.Watch(db, me, onlyMatched: false);
        var unfollowed = _seed.Watch(db, me, onlyMatched: false);
        var theirs = _seed.Watch(db, other, onlyMatched: false);
        var archived = _seed.ActiveAd(db);
        var ofUnfollowed = _seed.ActiveAd(db);
        var ofOther = _seed.ActiveAd(db);
        var live = _seed.ActiveAd(db);
        var secondLive = _seed.ActiveAd(db);
        _seed.Hit(db, me, archived, mine, T0.AddHours(1));
        _seed.Hit(db, me, ofUnfollowed, unfollowed, T0.AddHours(2));
        _seed.Hit(db, other, ofOther, theirs, T0.AddHours(3));
        _seed.Hit(db, me, live, mine, T0.AddHours(4));
        _seed.Hit(db, me, secondLive, mine, T0.AddHours(5));
        _seed.Archive(db, archived);
        _seed.Unfollow(db, unfollowed);

        var read = await ReadAsync(db, me);

        read.UnreadCount.ShouldBe(2);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(5));
        read.Items.Select(i => i.JobAdId).ShouldBe([secondLive.Value, live.Value]);
    }

    // The grade path's count is bounded by the token like the common path's, so a hit committed after
    // the token is neither counted nor acknowledged (the number stays what "Markera alla" clears).
    [Fact]
    public async Task LoadCountInput_OnTheGradePath_ReadsOnlyHitsUpToTheToken()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        _seed.Hit(db, userId, _seed.ActiveAd(db), onlyMatched, T0.AddHours(1));
        _seed.Hit(db, userId, _seed.ActiveAd(db), onlyMatched, T0.AddHours(2));
        _seed.Hit(db, userId, _seed.ActiveAd(db), onlyMatched, T0.AddHours(3));
        var ct = TestContext.Current.CancellationToken;
        var scope = await NewFollowedCompanyAdSet.LoadScopeAsync(db, userId, ct);

        var input = await NewFollowedCompanyAdSet.LoadCountInputAsync(
            db, userId, scope!, upperBound: T0.AddHours(2), maxGradeHits: 10, ct);

        input.Refused.ShouldBeFalse();
        input.GradeHits.Select(h => h.CreatedAt).OrderBy(c => c).ShouldBe([T0.AddHours(1), T0.AddHours(2)]);
    }

    // The bell's number is Översikt's "nya annonser från bevakade företag", not a second definition.
    [Fact]
    public async Task ReadAsync_UnreadCount_EqualsTheOversiktCount_ForTheSameState()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var plain = _seed.Watch(db, userId, onlyMatched: false);
        var onlyMatched = _seed.Watch(db, userId, onlyMatched: true);
        var seen = _seed.ActiveAd(db);
        var plainAd = _seed.ActiveAd(db);
        var gradedAd = _seed.ActiveAd(db);
        var belowGood = _seed.ActiveAd(db);
        var archived = _seed.ActiveAd(db);
        _seed.Hit(db, userId, seen, plain, T0.AddHours(1));
        _seed.SeeFollowedAdsThrough(db, seeker, T0.AddHours(1));
        _seed.Hit(db, userId, plainAd, plain, T0.AddHours(2));
        _seed.Hit(db, userId, plainAd, onlyMatched, T0.AddHours(2).AddMinutes(1));
        _seed.Hit(db, userId, gradedAd, onlyMatched, T0.AddHours(3));
        _seed.Hit(db, userId, belowGood, onlyMatched, T0.AddHours(4));
        _seed.Hit(db, userId, archived, plain, T0.AddHours(5));
        _seed.Archive(db, archived);
        GradeAsMatching(gradedAd);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var count = await new GetNewFollowedCompanyAdCountQueryHandler(db, currentUser, _profileBuilder, _perUserSearch)
            .Handle(new GetNewFollowedCompanyAdCountQuery(), TestContext.Current.CancellationToken);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(count.Count);
        read.UnreadCount.ShouldBe(2);
        read.Items.Count(i => i.IsUnread).ShouldBe(2);
    }
}
