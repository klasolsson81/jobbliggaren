using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Matching;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #1666 — the bell's handler: no user gives an empty feed, every source answers once, and the merge
/// keeps one row per (source, ad), newest first, cut at the newest horizon and capped.
/// </summary>
public class GetMyNotificationFeedQueryHandlerTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;

    private static NotificationFeedItemDto Item(
        NotificationFeedSourceKind kind, Guid adId, DateTimeOffset at, bool unread = true) =>
        new(kind, adId, "Roll", "Bolag AB", at, unread,
            kind == NotificationFeedSourceKind.Match ? NotifiableMatchGrade.Good : null);

    private static NotificationSourceRead Read(
        NotificationFeedSourceKind kind, int? unread, DateTimeOffset? horizon,
        params NotificationFeedItemDto[] items) =>
        new(kind, unread, items.Length == 0 ? null : items.Max(i => i.OccurredAt), items, horizon);

    [Fact]
    public async Task Handle_WithoutASignedInUser_ReturnsTheEmptyFeed()
    {
        using var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new GetMyNotificationFeedQueryHandler(
            db, currentUser, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

        var feed = await handler.Handle(new GetMyNotificationFeedQuery(), TestContext.Current.CancellationToken);

        feed.ShouldBeSameAs(NotificationFeedDto.Empty);
    }

    [Fact]
    public async Task Handle_AnswersOncePerSource_InListOrder()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        new NotificationFeedSeed().Seeker(db, userId, backgroundMatchConsent: true);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var handler = new GetMyNotificationFeedQueryHandler(
            db, currentUser, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

        var feed = await handler.Handle(new GetMyNotificationFeedQuery(), TestContext.Current.CancellationToken);

        feed.Sources.Select(s => s.Kind).ShouldBe(NotificationFeedSources.All.Select(s => s.Kind));
        feed.Sources.ShouldAllBe(s => s.UnreadCount == 0 && s.AcknowledgedThrough == null);
        feed.Items.ShouldBeEmpty();
    }

    // An ad that is both a match and a followed company's new ad is two notifications, for two reasons.
    [Fact]
    public void Merge_KeepsOneRowPerSourceAndAd_NewestFirst()
    {
        var shared = Guid.NewGuid();
        var other = Guid.NewGuid();
        var match = Read(NotificationFeedSourceKind.Match, 2, null,
            Item(NotificationFeedSourceKind.Match, shared, T0.AddHours(3)),
            Item(NotificationFeedSourceKind.Match, other, T0.AddHours(1)));
        var followed = Read(NotificationFeedSourceKind.FollowedCompanyAd, 1, null,
            Item(NotificationFeedSourceKind.FollowedCompanyAd, shared, T0.AddHours(2)));

        var feed = GetMyNotificationFeedQueryHandler.Merge([match, followed], maxItems: 20);

        feed.Items.Select(i => (i.Kind, i.JobAdId)).ShouldBe(
        [
            (NotificationFeedSourceKind.Match, shared),
            (NotificationFeedSourceKind.FollowedCompanyAd, shared),
            (NotificationFeedSourceKind.Match, other),
        ]);
        feed.Sources.Select(s => (s.Kind, s.UnreadCount)).ShouldBe(
        [
            (NotificationFeedSourceKind.Match, (int?)2),
            (NotificationFeedSourceKind.FollowedCompanyAd, (int?)1),
        ]);
    }

    [Fact]
    public void Merge_BreaksATieByKindThenAdId()
    {
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("00000000-0000-0000-0000-000000000002");
        var at = T0.AddHours(1);
        var match = Read(NotificationFeedSourceKind.Match, 2, null,
            Item(NotificationFeedSourceKind.Match, high, at),
            Item(NotificationFeedSourceKind.Match, low, at));
        var followed = Read(NotificationFeedSourceKind.FollowedCompanyAd, 1, null,
            Item(NotificationFeedSourceKind.FollowedCompanyAd, low, at));

        var feed = GetMyNotificationFeedQueryHandler.Merge([followed, match], maxItems: 20);

        feed.Items.Select(i => (i.Kind, i.JobAdId)).ShouldBe(
        [
            (NotificationFeedSourceKind.Match, low),
            (NotificationFeedSourceKind.Match, high),
            (NotificationFeedSourceKind.FollowedCompanyAd, low),
        ]);
    }

    // A windowed source may be missing rows at or before its horizon, so nothing that old is shown
    // from any source — an older match must not stand where a followed ad the window never read belongs.
    [Fact]
    public void Merge_CutsEverySourceAtTheNewestHorizon()
    {
        var match = Read(NotificationFeedSourceKind.Match, 2, null,
            Item(NotificationFeedSourceKind.Match, Guid.NewGuid(), T0.AddHours(5)),
            Item(NotificationFeedSourceKind.Match, Guid.NewGuid(), T0.AddHours(1)));
        var followed = Read(NotificationFeedSourceKind.FollowedCompanyAd, 70, horizon: T0.AddHours(2),
            Item(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid(), T0.AddHours(3)),
            Item(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid(), T0.AddHours(2)));

        var feed = GetMyNotificationFeedQueryHandler.Merge([match, followed], maxItems: 20);

        feed.Items.Select(i => i.OccurredAt).ShouldBe([T0.AddHours(5), T0.AddHours(3)]);
        feed.Sources.Single(s => s.Kind == NotificationFeedSourceKind.FollowedCompanyAd).UnreadCount.ShouldBe(70);
    }

    [Fact]
    public void Merge_CapsTheRows_ButNeverTheCounts()
    {
        var items = Enumerable.Range(1, 25)
            .Select(i => Item(NotificationFeedSourceKind.Match, Guid.NewGuid(), T0.AddMinutes(i)))
            .ToArray();
        var match = Read(NotificationFeedSourceKind.Match, 25, null, items);

        var feed = GetMyNotificationFeedQueryHandler.Merge([match], maxItems: 20);

        feed.Items.Count.ShouldBe(20);
        feed.Items[0].OccurredAt.ShouldBe(T0.AddMinutes(25));
        feed.Sources.ShouldHaveSingleItem().UnreadCount.ShouldBe(25);
    }

    [Fact]
    public void Merge_PassesARefusedCountThroughAsNull()
    {
        var followed = Read(NotificationFeedSourceKind.FollowedCompanyAd, unread: null, null,
            Item(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid(), T0.AddHours(1)));

        var feed = GetMyNotificationFeedQueryHandler.Merge([followed], maxItems: 20);

        feed.Sources.ShouldHaveSingleItem().UnreadCount.ShouldBeNull();
        feed.Items.ShouldHaveSingleItem();
    }
}
