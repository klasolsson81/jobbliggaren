using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #1666 — the bell's match source. Branch logic under EF InMemory: the consent gate (B1, ADR 0080
/// Beslut 5) in both directions, the token-first protocol, the unread flag against the watermark,
/// the lifecycle gate inherited from <c>NewMatchSet</c>, owner scope, and parity with the uncapped
/// new-match count. The Npgsql translation of the same statements is <c>NotificationFeedEndpointTests</c>'s.
/// </summary>
public class MatchFeedSourceTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();

    private static NotificationFeedReadContext Context(AppDbContext db, Guid userId) =>
        new(db, userId, MaxItems: 20,
            Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

    private static Task<NotificationSourceRead> ReadAsync(AppDbContext db, Guid userId) =>
        new MatchFeedSource().ReadAsync(Context(db, userId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReadAsync_WithConsentAndNoWatermark_ListsEveryMatchAsUnread_NewestFirst()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var older = _seed.ActiveAd(db, "Äldre roll", "Äldre AB");
        var newer = _seed.ActiveAd(db, "Nyare roll", "Nyare AB");
        _seed.Match(db, userId, older, NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.Match(db, userId, newer, NotifiableMatchGrade.Top, T0.AddHours(2));

        var read = await ReadAsync(db, userId);

        read.Kind.ShouldBe(NotificationFeedSourceKind.Match);
        read.UnreadCount.ShouldBe(2);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.Horizon.ShouldBeNull();
        read.Items.Select(i => i.JobAdId).ShouldBe([newer.Value, older.Value]);
        read.Items[0].Title.ShouldBe("Nyare roll");
        read.Items[0].CompanyName.ShouldBe("Nyare AB");
        read.Items[0].Grade.ShouldBe(NotifiableMatchGrade.Top);
        read.Items.ShouldAllBe(i => i.IsUnread);
    }

    [Fact]
    public async Task ReadAsync_AfterTheWatermark_FlagsOnlyNewerMatchesUnread_AndCountsOnlyThem()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var seen = _seed.ActiveAd(db);
        var fresh = _seed.ActiveAd(db);
        _seed.Match(db, userId, seen, NotifiableMatchGrade.Strong, T0.AddHours(1));
        _seed.Match(db, userId, fresh, NotifiableMatchGrade.Good, T0.AddHours(3));
        _seed.SeeMatchesThrough(db, seeker, T0.AddHours(2));

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(3));
        read.Items.Single(i => i.JobAdId == fresh.Value).IsUnread.ShouldBeTrue();
        read.Items.Single(i => i.JobAdId == seen.Value).IsUnread.ShouldBeFalse();
    }

    [Fact]
    public async Task ReadAsync_WithEverythingAcknowledged_StillListsReadRows_WithNothingUnread()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, userId, ad, NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.SeeMatchesThrough(db, seeker, T0.AddHours(1));

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(0);
        read.AcknowledgedThrough.ShouldBeNull();
        read.Items.ShouldHaveSingleItem().IsUnread.ShouldBeFalse();
    }

    // B1 (security-auditor, #1666 form round): a withdrawal leaves the match rows in place, and the
    // bell is a notification surface, so it must go quiet the moment consent is withdrawn.
    [Fact]
    public async Task ReadAsync_AfterConsentIsWithdrawn_SaysNothing_ThenSpeaksAgainWhenItIsGivenBack()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var ad = _seed.ActiveAd(db);
        _seed.Match(db, userId, ad, NotifiableMatchGrade.Top, T0.AddHours(1));

        (await ReadAsync(db, userId)).UnreadCount.ShouldBe(1);

        _seed.SetConsent(db, seeker, enabled: false, T0.AddHours(2));
        var withdrawn = await ReadAsync(db, userId);
        withdrawn.UnreadCount.ShouldBe(0);
        withdrawn.AcknowledgedThrough.ShouldBeNull();
        withdrawn.Items.ShouldBeEmpty();
        db.UserJobAdMatches.Count(m => m.UserId == userId).ShouldBe(1, "the rows stay; only the bell goes quiet");

        _seed.SetConsent(db, seeker, enabled: true, T0.AddHours(3));
        var regiven = await ReadAsync(db, userId);
        regiven.UnreadCount.ShouldBe(1);
        regiven.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ReadAsync_ForAUserWhoNeverConsented_SaysNothing()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);

        var read = await ReadAsync(db, userId);

        read.ShouldBe(NotificationSourceRead.Nothing(NotificationFeedSourceKind.Match));
    }

    [Fact]
    public async Task ReadAsync_SkipsAMatchWhoseAdWasArchived_InTheCountAndTheList()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var live = _seed.ActiveAd(db);
        var secondLive = _seed.ActiveAd(db);
        var archived = _seed.ActiveAd(db);
        _seed.Match(db, userId, live, NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.Match(db, userId, secondLive, NotifiableMatchGrade.Good, T0.AddHours(2));
        _seed.Match(db, userId, archived, NotifiableMatchGrade.Strong, T0.AddHours(3));
        _seed.Archive(db, archived);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(2);
        read.AcknowledgedThrough.ShouldBe(T0.AddHours(2));
        read.Items.Select(i => i.JobAdId).ShouldNotContain(archived.Value);
        read.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReadAsync_ReadsOnlyTheSignedInUsersMatches()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        _seed.Seeker(db, me, backgroundMatchConsent: true);
        _seed.Seeker(db, other, backgroundMatchConsent: true);
        _seed.Match(db, other, _seed.ActiveAd(db), NotifiableMatchGrade.Top, T0.AddHours(1));

        var read = await ReadAsync(db, me);

        read.UnreadCount.ShouldBe(0);
        read.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadAsync_ListsAtMostMaxItems_ButCountsEveryUnreadMatch()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        for (var i = 0; i < 25; i++)
            _seed.Match(db, userId, _seed.ActiveAd(db), NotifiableMatchGrade.Good, T0.AddMinutes(i + 1));

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(25);
        read.Items.Count.ShouldBe(20);
        read.Items[0].OccurredAt.ShouldBe(T0.AddMinutes(25));
    }

    // The bell's number is the uncapped new-match count (ADR 0080 Beslut 6), not a second definition.
    [Fact]
    public async Task ReadAsync_UnreadCount_EqualsTheNewMatchCount_ForTheSameState()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = _seed.Seeker(db, userId, backgroundMatchConsent: true);
        _seed.Match(db, userId, _seed.ActiveAd(db), NotifiableMatchGrade.Good, T0.AddHours(1));
        _seed.Match(db, userId, _seed.ActiveAd(db), NotifiableMatchGrade.Top, T0.AddHours(3));
        _seed.Match(db, userId, _seed.ActiveAd(db), NotifiableMatchGrade.Strong, T0.AddHours(4));
        var archived = _seed.ActiveAd(db);
        _seed.Match(db, userId, archived, NotifiableMatchGrade.Strong, T0.AddHours(5));
        _seed.Archive(db, archived);
        _seed.SeeMatchesThrough(db, seeker, T0.AddHours(2));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var count = await new GetMyNewMatchCountQueryHandler(db, currentUser)
            .Handle(new GetMyNewMatchCountQuery(), TestContext.Current.CancellationToken);

        var read = await ReadAsync(db, userId);

        read.UnreadCount.ShouldBe(count.Count);
        read.UnreadCount.ShouldBe(2);
    }
}
