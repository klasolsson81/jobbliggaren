using Jobbliggaren.Application.Common.Abstractions;
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
/// #2130 — the followed-company source's per-row read state (ADR 0162 as amended): an ad is unread in the bell when ANY
/// included hit of it is new since the watermark and not read in the bell, the token stays the newest of every new hit,
/// the user's own switch gates the source (no watches are read without it), and the newest unread ads are named on EVERY
/// path when the window leaves them out: on the common path by one grouped statement, on the grade path from the counted
/// hits with no second grading call. Branch logic under EF InMemory; the Npgsql translation is
/// <c>NotificationFeedReadStateEndpointTests</c>'s.
/// </summary>
public class FollowedCompanyAdFeedSourceReadStateTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();
    private readonly IMatchProfileBuilder _profileBuilder = Substitute.For<IMatchProfileBuilder>();
    private readonly IPerUserJobAdSearchQuery _perUserSearch = Substitute.For<IPerUserJobAdSearchQuery>();

    public FollowedCompanyAdFeedSourceReadStateTests() =>
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));

    private void GradeAsMatching(params JobAdId[] matching) =>
        _perUserSearch.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<JobAdId>(matching));

    private Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId, int maxItems = 20) =>
        new FollowedCompanyAdFeedSource().ReadBoundedAsync(
            new NotificationFeedReadContext(db, userId, maxItems, _profileBuilder, _perUserSearch),
            FollowedCompanyAdFeedSource.MaxCountedHits,
            TestContext.Current.CancellationToken);

    // The definition as a table: type on or off, a hit past the watermark or not, read in the bell or not.
    public static TheoryData<bool, bool, bool> EveryCombination()
    {
        var cases = new TheoryData<bool, bool, bool>();
        bool[] both = [true, false];
        foreach (var typeOn in both)
            foreach (var pastWatermark in both)
                foreach (var readInBell in both)
                    cases.Add(typeOn, pastWatermark, readInBell);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public async Task ReadAsync_OverEveryCombination_CountsAnUnreadAdExactlyWhenTheTypeIsOnTheHitIsNewAndNotRead(
        bool typeOn, bool pastWatermark, bool readInBell)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watch, T0.AddHours(2));
        _seed.SeeFollowedAdsThrough(db, seeker, pastWatermark ? T0.AddHours(1) : T0.AddHours(3));
        if (readInBell)
            NotificationFeedSeed.HitsReadInBell(db, userId, ad);
        if (!typeOn)
            _seed.SwitchFeedFollowedAds(db, seeker, on: false, T0.AddHours(4));

        var read = await ReadAsync(db, userId);

        if (!typeOn)
        {
            read.ShouldBe(NotificationSourceRead.Nothing(NotificationFeedSourceKind.FollowedCompanyAd));
            return;
        }

        var unread = pastWatermark && !readInBell;
        read.UnreadCount.ShouldBe(unread ? 1 : 0);
        read.AcknowledgedThrough.ShouldBe(pastWatermark ? T0.AddHours(2) : null,
            "the token is the newest new hit, whether or not it is read in the bell");
        read.Items.Single().IsUnread.ShouldBe(unread);
    }

    [Fact]
    public async Task ReadAsync_WithTheTypeOff_ReadsNoWatchAndNoHit_AndOtherUsersTypesAreTheirOwn()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, me, onlyMatched: false);
        _seed.Hit(db, me, _seed.ActiveAd(db), watch, T0.AddHours(1));
        _seed.SwitchFeedFollowedAds(db, mine, on: false, T0.AddHours(2));
        var othersWatch = _seed.Watch(db, other, onlyMatched: false);
        _seed.Hit(db, other, _seed.ActiveAd(db), othersWatch, T0.AddHours(1));

        (await ReadAsync(db, me)).ShouldBe(NotificationSourceRead.Nothing(NotificationFeedSourceKind.FollowedCompanyAd));
        (await ReadAsync(db, other)).UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_ForAnAdThatIsNoMatch_IsNotAffectedByTheBackgroundMatchSwitchOrConsent()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        _seed.Hit(db, userId, _seed.ActiveAd(db), watch, T0.AddHours(1));
        _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(2));
        _seed.SetConsent(db, seeker, enabled: false, T0.AddHours(3));

        (await ReadAsync(db, userId)).UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task ReadAsync_WithTheNewestHitReadInTheBell_KeepsTheTokenAtThatHit()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var older = _seed.ActiveAd(db);
        var newest = _seed.ActiveAd(db);
        _seed.Hit(db, userId, older, watch, T0.AddHours(1));
        _seed.Hit(db, userId, newest, watch, T0.AddHours(2));
        NotificationFeedSeed.HitsReadInBell(db, userId, newest);

        var read = await ReadAsync(db, userId);

        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.UnreadCount.ShouldBe(1);
        read.Items.Single(i => i.JobAdId == newest.Value).IsUnread.ShouldBeFalse();
        read.Items.Single(i => i.JobAdId == older.Value).IsUnread.ShouldBeTrue();
    }

    // An ad reached through two watches is two hits and one row (two active watches on one employer, ADR 0162 D3b:
    // code-producible, the brand-group catalogue being empty today). A click reads both. The switch-on mark reads the hits
    // created after the type's off-stamp and leaves the older ones, so an older unread hit can sit beside a newer read one,
    // and the ad is unread while ANY included new hit is: the count's own predicate, so the row and the count beside it
    // cannot disagree.
    [Fact]
    public async Task ReadAsync_WithAnOlderUnreadHitBesideANewerReadOne_ListsOneUnreadRowAtTheNewestMoment()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, userId, onlyMatched: false);
        var watchB = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, userId, ad, watchB, T0.AddHours(3));
        NotificationFeedSeed.HitReadInBell(db, userId, ad, watchB);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        var item = read.Items.ShouldHaveSingleItem();
        item.IsUnread.ShouldBeTrue();
        item.OccurredAt.ShouldBe(T0.AddHours(3), "a row's moment is its newest included hit, read or not (ADR 0162 D2)");
    }

    [Fact]
    public async Task ReadAsync_WithEveryHitOfAnAdReadInTheBell_ListsTheAdRead()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, userId, onlyMatched: false);
        var watchB = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, userId, ad, watchB, T0.AddHours(2));
        NotificationFeedSeed.HitsReadInBell(db, userId, ad);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(0);
        read.Items.ShouldHaveSingleItem().IsUnread.ShouldBeFalse();
    }

    // #2151: a dismissed hit is read AND dismissed. It is neither listed nor counted, whatever its age.
    [Fact]
    public async Task ReadAsync_WithEveryHitOfAnAdDismissed_ListsNothingCountsNothingAndKeepsTheTokenAtThatHit()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, userId, onlyMatched: false);
        var watchB = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, userId, ad, watchB, T0.AddHours(2));
        NotificationFeedSeed.HitsDismissed(db, userId, ad);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(0);
        read.Items.ShouldBeEmpty();
        // The token has no flag predicate (ADR 0168 D2), so "Markera alla" still covers everything up to the dismissed hit.
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
    }

    [Fact]
    public async Task ReadAsync_WithADismissedAdBesideAReadOneAndAnUnreadOne_ListsOnlyTheLatterTwo()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var dismissed = _seed.ActiveAd(db, "Borttagen");
        var read = _seed.ActiveAd(db, "Läst");
        var unread = _seed.ActiveAd(db, "Oläst");
        _seed.Hit(db, userId, dismissed, watch, T0.AddHours(3));
        _seed.Hit(db, userId, read, watch, T0.AddHours(2));
        _seed.Hit(db, userId, unread, watch, T0.AddHours(1));
        NotificationFeedSeed.HitsDismissed(db, userId, dismissed);
        NotificationFeedSeed.HitsReadInBell(db, userId, read);

        var result = await ReadAsync(db, userId);

        result.UnreadCount.ShouldBe(1);
        result.Items.Select(i => i.Title).ToList().ShouldBe(["Läst", "Oläst"]);
        result.Items.Single(i => i.Title == "Läst").IsUnread.ShouldBeFalse();
    }

    // ADR 0169 s1, the read side: the state is one dismissed hit and one NEWER hit for the same ad through another watch. The
    // actor that produces it is the boundary race named in s1 (an ad loaded by two scan runs while the user follows its
    // organisation a second way between them); the scan job's ordinary case cannot (CompanyWatchScanJobIntegrationTests).
    // The ad then reads as ONE unread row, at its undismissed hit: the residual the ADR states, not a defect to hide.
    [Fact]
    public async Task ReadAsync_WithADismissedHitAndANewerUndismissedHitOfTheSameAd_ListsTheAdOnceAsUnread()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, userId, onlyMatched: false);
        var watchB = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, userId, ad, watchB, T0.AddHours(2));
        db.FollowedCompanyAdHits.Single(h => h.CompanyWatchId == watchA).DismissFromNotificationFeed();
        db.SaveChanges();

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        var row = read.Items.ShouldHaveSingleItem();
        row.JobAdId.ShouldBe(ad.Value);
        row.IsUnread.ShouldBeTrue();
        row.OccurredAt.ShouldBe(T0.AddHours(2));
    }

    // Art. 5(1)(c): a dismissed ad never reaches the grader. On the grade path (a watch with "endast matchade") the window and
    // the counted hits both leave dismissed hits out, so the one grading call carries only the live ad.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_NeverSendsADismissedAdToTheGrader()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: true);
        var dismissed = _seed.ActiveAd(db, "Borttagen");
        var live = _seed.ActiveAd(db, "Kvar");
        _seed.Hit(db, userId, dismissed, watch, T0.AddHours(2));
        _seed.Hit(db, userId, live, watch, T0.AddHours(1));
        NotificationFeedSeed.HitsDismissed(db, userId, dismissed);
        GradeAsMatching(live);

        var read = await ReadAsync(db, userId);

        read.Items.Select(i => i.JobAdId).ToList().ShouldBe([live.Value]);
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Count == 1 && ids.Contains(live)),
            Arg.Any<CancellationToken>());
    }

    // The common path under-fill (named skip of #2130 PR 1, closed here): no "endast matchade" watch, so every hit is
    // included and the old grade-path reserve never ran. Read ads crowd the newest-first window; the older unread ad is
    // named by the grouped statement. The window reads HitsPerRow x MaxItems hits (3 x 2 here).
    [Fact]
    public async Task ReadAsync_OnTheCommonPath_WhenReadAdsCrowdAnUnreadOneOutOfTheWindow_StillListsIt()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var unreadAd = _seed.ActiveAd(db, "Oläst annons");
        _seed.Hit(db, userId, unreadAd, watch, T0.AddHours(1));
        for (var i = 0; i < 8; i++)
        {
            var readAd = _seed.ActiveAd(db, $"Läst annons {i}");
            _seed.Hit(db, userId, readAd, watch, T0.AddHours(2 + i));
            NotificationFeedSeed.HitsReadInBell(db, userId, readAd);
        }

        var read = await ReadAsync(db, userId, maxItems: 2);

        read.UnreadCount.ShouldBe(1);
        read.Items.Count(i => i.IsUnread).ShouldBe(1);
        var unread = read.Items.Single(i => i.IsUnread);
        unread.JobAdId.ShouldBe(unreadAd.Value);
        unread.Title.ShouldBe("Oläst annons");
        read.Items.Select(i => i.JobAdId).Distinct().Count().ShouldBe(read.Items.Count);
    }

    [Fact]
    public async Task ReadAsync_OnTheCommonPath_NamesTheNewestUnreadAdsFirst_AtTheCapAndNoMore()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var ads = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var ad = _seed.ActiveAd(db, $"Annons {i}");
            ads.Add(ad);
            _seed.Hit(db, userId, ad, watch, T0.AddHours(1 + i));
        }
        // 8 read ads newer than all four, so the window (6 hits) holds read ads only.
        for (var i = 0; i < 8; i++)
        {
            var readAd = _seed.ActiveAd(db);
            _seed.Hit(db, userId, readAd, watch, T0.AddHours(10 + i));
            NotificationFeedSeed.HitsReadInBell(db, userId, readAd);
        }

        var read = await ReadAsync(db, userId, maxItems: 2);

        read.UnreadCount.ShouldBe(4);
        read.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([ads[3].Value, ads[2].Value]);
    }

    // The grade path. An "endast matchade" watch whose newest hits all grade below Good fills the window with excluded
    // hits; the unread ads that do match sit further back and are named from the counted hits with no second grading call.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_WithReadRowsAndExcludedHitsFillingTheWindow_ListsTheNewestIncludedUnreadAds()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var graded = _seed.Watch(db, userId, onlyMatched: true);
        var included = _seed.ActiveAd(db, "Matchande annons");
        _seed.Hit(db, userId, included, graded, T0.AddHours(1));
        // The excluded hits the window fills with are the ones the user already read in the bell.
        for (var i = 0; i < 8; i++)
        {
            var excluded = _seed.ActiveAd(db);
            _seed.Hit(db, userId, excluded, graded, T0.AddHours(2 + i));
            NotificationFeedSeed.HitsReadInBell(db, userId, excluded);
        }
        GradeAsMatching(included);

        var read = await ReadAsync(db, userId, maxItems: 2);

        read.UnreadCount.ShouldBe(1);
        read.Items.ShouldHaveSingleItem().JobAdId.ShouldBe(included.Value);
        read.Items.Single().IsUnread.ShouldBeTrue();
        await _perUserSearch.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_OnTheGradePath_NeverCountsOrNamesAHitReadInTheBell()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var graded = _seed.Watch(db, userId, onlyMatched: true);
        var readAd = _seed.ActiveAd(db);
        var unreadAd = _seed.ActiveAd(db);
        _seed.Hit(db, userId, unreadAd, graded, T0.AddHours(1));
        _seed.Hit(db, userId, readAd, graded, T0.AddHours(2));
        NotificationFeedSeed.HitsReadInBell(db, userId, readAd);
        GradeAsMatching(readAd, unreadAd);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.Items.Single(i => i.JobAdId == readAd.Value).IsUnread.ShouldBeFalse();
        read.Items.Single(i => i.JobAdId == unreadAd.Value).IsUnread.ShouldBeTrue();
    }

    // The grade path's ordering details (#2130 PR 1's named skip, closed here). The fallback names an ad at its NEWEST
    // included hit, so an ad with two hits sorts by the newer one; a mutant that took the oldest hit reverses these two.
    // Two hits of one ad are two active watches on one employer (ADR 0162 D3b: code-producible, the brand-group catalogue
    // being empty today).
    [Fact]
    public async Task ReadAsync_OnTheGradePath_NamesAnAdAtItsNewestIncludedHit_NotItsOldest()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var first = _seed.Watch(db, userId, onlyMatched: true);
        var second = _seed.Watch(db, userId, onlyMatched: true);
        var twoHits = _seed.ActiveAd(db, "Två träffar");
        var oneHit = _seed.ActiveAd(db, "En träff");
        _seed.Hit(db, userId, twoHits, first, T0.AddMinutes(1));
        _seed.Hit(db, userId, oneHit, first, T0.AddMinutes(3));
        _seed.Hit(db, userId, twoHits, second, T0.AddMinutes(5));
        for (var i = 0; i < 10; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db), first, T0.AddHours(1 + i));
        GradeAsMatching(twoHits, oneHit);

        var read = await ReadAsync(db, userId, maxItems: 3);

        read.UnreadCount.ShouldBe(2);
        read.Items.Select(i => i.Title).ShouldBe(["Två träffar", "En träff"]);
        read.Items[0].OccurredAt.ShouldBe(T0.AddMinutes(5));
        read.Items.ShouldAllBe(i => i.IsUnread);
    }

    // An ad is named at its newest INCLUDED hit: a newer hit through an "endast matchade" watch the grader excludes does not
    // move it. A mutant that groups before the inclusion rule names it at the excluded hit's moment and swaps the order.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_NamesAnAdAtItsNewestIncludedHit_NotAtANewerExcludedOne()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var regular = _seed.Watch(db, userId, onlyMatched: false);
        var graded = _seed.Watch(db, userId, onlyMatched: true);
        var mixed = _seed.ActiveAd(db, "Vanlig och utesluten");
        var rival = _seed.ActiveAd(db, "Rival");
        _seed.Hit(db, userId, mixed, regular, T0.AddMinutes(1));   // a regular watch includes every hit
        _seed.Hit(db, userId, mixed, graded, T0.AddMinutes(9));    // the grader does not match this ad: excluded
        _seed.Hit(db, userId, rival, regular, T0.AddMinutes(5));
        for (var i = 0; i < 10; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db), graded, T0.AddHours(1 + i));
        GradeAsMatching();

        var read = await ReadAsync(db, userId, maxItems: 3);

        read.UnreadCount.ShouldBe(2);
        read.Items.Select(i => i.Title).ShouldBe(["Rival", "Vanlig och utesluten"]);
        read.Items[1].OccurredAt.ShouldBe(T0.AddMinutes(1));
    }

    // Seven ads share one moment and the cap is three: which three are named is decided by the ad id, ascending, and not by
    // the order the rows happen to come back in. The same ids are named whatever order they were created in.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_WhenMoreAdsShareTheNewestMomentThanTheCapTakes_TheCapFallsOnTheLowestAdIds()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var graded = _seed.Watch(db, userId, onlyMatched: true);
        var tied = Enumerable.Range(1, 7).Select(i => _seed.ActiveAd(db, $"Samtidig {i}")).ToArray();
        foreach (var ad in tied)
            _seed.Hit(db, userId, ad, graded, T0.AddMinutes(30));
        for (var i = 0; i < 10; i++)
            _seed.Hit(db, userId, _seed.ActiveAd(db), graded, T0.AddHours(1 + i));
        GradeAsMatching(tied);

        var read = await ReadAsync(db, userId, maxItems: 3);

        read.UnreadCount.ShouldBe(7);
        read.Items.Select(i => i.JobAdId).ShouldBe(tied.Select(a => a.Value).Order().Take(3));
        read.Items.ShouldAllBe(i => i.OccurredAt == T0.AddMinutes(30));
    }

    // An ad the window lists as READ (its newest hit is read) whose older hit is unread and beyond the window: the fallback
    // makes the row unread and keeps the LATER of the two moments, so it does not sink below rows it is newer than. The
    // state is the switch-on mark's: it reads the hits created after the off-stamp and leaves the older ones.
    [Fact]
    public async Task ReadAsync_OnTheGradePath_TheUnreadFallbackRowKeepsTheLaterMomentOfTheWindowsRowOfTheSameAd()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var older = _seed.Watch(db, userId, onlyMatched: true);
        var newer = _seed.Watch(db, userId, onlyMatched: true);
        var ad = _seed.ActiveAd(db, "Delvis läst");
        _seed.Hit(db, userId, ad, older, T0.AddMinutes(1));
        _seed.Hit(db, userId, ad, newer, T0.AddMinutes(10));
        NotificationFeedSeed.HitReadInBell(db, userId, ad, newer);
        // maxItems 1 reads a window of the newest three hits, and the fourth sets the horizon: the ad's newest hit is in
        // the window, its older hit is not.
        _seed.Hit(db, userId, _seed.ActiveAd(db), older, T0.AddMinutes(5));
        _seed.Hit(db, userId, _seed.ActiveAd(db), older, T0.AddMinutes(11));
        _seed.Hit(db, userId, _seed.ActiveAd(db), older, T0.AddMinutes(12));
        GradeAsMatching(ad);

        var read = await ReadAsync(db, userId, maxItems: 1);

        read.UnreadCount.ShouldBe(1);
        var row = read.Items.Single(i => i.JobAdId == ad.Value);
        row.IsUnread.ShouldBeTrue();
        row.OccurredAt.ShouldBe(T0.AddMinutes(10));
    }

    [Fact]
    public async Task ReadAsync_IsOwnerScoped_AHitReadByAnotherUserDoesNotChangeMine()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        var ad = _seed.ActiveAd(db);
        var myWatch = _seed.Watch(db, me, onlyMatched: false);
        var othersWatch = _seed.Watch(db, other, onlyMatched: false);
        _seed.Hit(db, me, ad, myWatch, T0.AddHours(1));
        _seed.Hit(db, other, ad, othersWatch, T0.AddHours(1));
        NotificationFeedSeed.HitsReadInBell(db, other, ad);

        var read = await ReadAsync(db, me);

        read.UnreadCount.ShouldBe(1);
        read.Items.Single().IsUnread.ShouldBeTrue();
    }
}
