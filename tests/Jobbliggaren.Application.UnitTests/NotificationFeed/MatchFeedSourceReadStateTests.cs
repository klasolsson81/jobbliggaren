using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #2130 — the match source's per-row read state (ADR 0162 as amended): a row is unread in the bell when it is new since
/// the watermark AND not read in the bell, the token stays the newest of the watermark-new set, the user's own switch
/// gates the source with the consent (setting AND consent, never OR), and unread rows read-state crowded out of the
/// newest-first window are named by their own statement, each ad once. Branch logic under EF InMemory; the Npgsql
/// translation of the same statements is <c>NotificationFeedReadStateEndpointTests</c>'s.
/// </summary>
public class MatchFeedSourceReadStateTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();

    private static NotificationFeedReadContext Context(AppDbContext db, Guid userId, int maxItems = 20) =>
        new(db, userId, maxItems, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

    private static Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId, int maxItems = 20) =>
        new MatchFeedSource().ReadAsync(Context(db, userId, maxItems), TestContext.Current.CancellationToken);

    // The definition, as a table: every combination of the four things that decide a match row's place in the bell.
    // The expected values are read off the rule, not off the code: a row COUNTS and is listed unread iff the consent is
    // granted, the type is on, the row is past the watermark and it is not read in the bell; with the consent or the
    // type off the source says nothing at all.
    public static TheoryData<bool, bool, bool, bool> EveryCombination()
    {
        var cases = new TheoryData<bool, bool, bool, bool>();
        bool[] both = [true, false];
        foreach (var consent in both)
            foreach (var typeOn in both)
                foreach (var pastWatermark in both)
                    foreach (var readInBell in both)
                        cases.Add(consent, typeOn, pastWatermark, readInBell);
        return cases;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public async Task ReadAsync_OverEveryCombination_CountsAndListsAnUnreadRowExactlyWhenAllFourHold(
        bool consent, bool typeOn, bool pastWatermark, bool readInBell)
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, userId, ad, NotifiableMatchGrade.Strong, T0.AddHours(2));
        // The watermark sits before the row (new) or after it (already acknowledged through its destination).
        _seed.SeeMatchesThrough(db, seeker, pastWatermark ? T0.AddHours(1) : T0.AddHours(3));
        if (readInBell)
            NotificationFeedSeed.MatchReadInBell(db, userId, ad);
        if (!typeOn)
            _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(4));
        if (!consent)
            _seed.SetConsent(db, seeker, enabled: false, T0.AddHours(5));

        var read = await ReadAsync(db, userId);

        if (!consent || !typeOn)
        {
            read.ShouldBe(NotificationSourceRead.Nothing(NotificationFeedSourceKind.Match));
            return;
        }

        var unread = pastWatermark && !readInBell;
        read.UnreadCount.ShouldBe(unread ? 1 : 0);
        read.AcknowledgedThrough.ShouldBe(pastWatermark ? T0.AddHours(2) : null,
            "the token is the newest watermark-new row, whether or not it is read in the bell");
        // The row is listed either way (the watermark bounds the list when nothing is new), unread only when all hold.
        read.Items.Single().IsUnread.ShouldBe(unread);
    }

    [Fact]
    public async Task ReadAsync_WithTheTypeOff_ReadsNoMatchRowAtAll()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        _seed.Match(db, userId, _seed.ActiveAd(db), NotifiableMatchGrade.Top, T0.AddHours(1));
        _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(2));

        var read = await ReadAsync(db, userId);

        // Not "zero unread with rows": the source was not read, so there is no list, no count and no token.
        read.UnreadCount.ShouldBe(0);
        read.AcknowledgedThrough.ShouldBeNull();
        read.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAsync_WhenTheTypeIsSwitchedBackOn_ListsTheRowAgain()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, userId, ad, NotifiableMatchGrade.Top, T0.AddHours(1));
        _seed.SwitchFeedMatches(db, seeker, on: false, T0.AddHours(2));
        _seed.SwitchFeedMatches(db, seeker, on: true, T0.AddHours(3));

        var read = await ReadAsync(db, userId);

        // What was unread before the switch-off is unread again: a switch only decides what reaches the bell.
        read.UnreadCount.ShouldBe(1);
        read.Items.Single().JobAdId.ShouldBe(ad.Value);
        read.Items.Single().IsUnread.ShouldBeTrue();
    }

    // The token stands for "everything up to here", so a row read one by one can never leave the rest above a token
    // computed over unread rows only: newest row read, an older one unread -> the token is still the newest row's.
    [Fact]
    public async Task ReadAsync_WithTheNewestRowReadInTheBell_KeepsTheTokenAtThatRow()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var older = _seed.ActiveAd(db);
        var newest = _seed.ActiveAd(db);
        _seed.Match(db, userId, older, NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.Match(db, userId, newest, NotifiableMatchGrade.Top, T0.AddHours(2));
        NotificationFeedSeed.MatchReadInBell(db, userId, newest);

        var read = await ReadAsync(db, userId);

        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.UnreadCount.ShouldBe(1);
        read.Items.Single(i => i.JobAdId == newest.Value).IsUnread.ShouldBeFalse();
        read.Items.Single(i => i.JobAdId == older.Value).IsUnread.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadAsync_WhenReadRowsCrowdAnUnreadOneOutOfTheWindow_ListsItOnce()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        // One unread match, then three newer ones the user has read in the bell, with room for only three rows.
        var unreadAd = _seed.ActiveAd(db, "Oläst roll");
        _seed.Match(db, userId, unreadAd, NotifiableMatchGrade.Good, T0.AddHours(1));
        for (var i = 0; i < 3; i++)
        {
            var read = _seed.ActiveAd(db, $"Läst roll {i}");
            _seed.Match(db, userId, read, NotifiableMatchGrade.Strong, T0.AddHours(2 + i));
            NotificationFeedSeed.MatchReadInBell(db, userId, read);
        }

        var result = await ReadAsync(db, userId, maxItems: 3);

        result.UnreadCount.ShouldBe(1);
        // The newest-first window holds the three read rows; the unread one, older than all of them, is named by the
        // second statement. Three read rows plus the unread one, and no ad twice.
        result.Items.Count(i => i.IsUnread).ShouldBe(1);
        result.Items.Single(i => i.IsUnread).JobAdId.ShouldBe(unreadAd.Value);
        result.Items.Select(i => i.JobAdId).Distinct().Count().ShouldBe(result.Items.Count);
    }

    // More unread rows are crowded out than the cap takes: the second statement names the NEWEST of them, not the oldest.
    [Fact]
    public async Task ReadAsync_WhenMoreUnreadRowsAreCrowdedOutThanTheCapTakes_NamesTheNewestUnreadOnes()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        for (var i = 1; i <= 5; i++)
            _seed.Match(db, userId, _seed.ActiveAd(db, $"Oläst {i}"), NotifiableMatchGrade.Good, T0.AddHours(i));
        for (var i = 0; i < 3; i++)
        {
            var read = _seed.ActiveAd(db, $"Läst {i}");
            _seed.Match(db, userId, read, NotifiableMatchGrade.Strong, T0.AddHours(10 + i));
            NotificationFeedSeed.MatchReadInBell(db, userId, read);
        }

        var result = await ReadAsync(db, userId, maxItems: 3);

        result.UnreadCount.ShouldBe(5);
        result.Items.Where(i => i.IsUnread).Select(i => i.Title).Order().ShouldBe(["Oläst 3", "Oläst 4", "Oläst 5"]);
    }

    // The other half of F3: the e-mail state machine never clears a read mark. The mark itself is written by one set-based
    // statement in production (Npgsql's tests); here it is set through the change tracker, the same boolean.
    [Fact]
    public async Task TheMailPath_NeverClearsAReadMark()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, userId, ad, NotifiableMatchGrade.Top, T0.AddHours(1));
        NotificationFeedSeed.MatchReadInBell(db, userId, ad);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(T0.AddHours(2));

        var match = db.UserJobAdMatches.Single();
        match.MarkQueued();
        match.MarkSent(clock);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.UserJobAdMatches.Single().ReadInNotificationFeed.ShouldBeTrue("sending the e-mail never clears a read mark");
    }

    [Fact]
    public async Task ReadAsync_WhenTheWindowAlreadyHoldsEveryUnreadRow_DoesNotListAnAdTwice()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var first = _seed.ActiveAd(db);
        var second = _seed.ActiveAd(db);
        _seed.Match(db, userId, first, NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.Match(db, userId, second, NotifiableMatchGrade.Top, T0.AddHours(2));

        var result = await ReadAsync(db, userId);

        result.Items.Select(i => i.JobAdId).ShouldBe([second.Value, first.Value]);
    }

    [Fact]
    public async Task ReadAsync_IsOwnerScoped_AMatchReadByAnotherUserDoesNotChangeMine()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        _seed.Seeker(db, me, backgroundMatchConsent: true);
        _seed.Seeker(db, other, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, me, ad, NotifiableMatchGrade.Top, T0.AddHours(1));
        _seed.Match(db, other, ad, NotifiableMatchGrade.Top, T0.AddHours(1));
        NotificationFeedSeed.MatchReadInBell(db, other, ad);

        var read = await ReadAsync(db, me);

        read.UnreadCount.ShouldBe(1);
        read.Items.Single().IsUnread.ShouldBeTrue();
    }
}
