using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationFeedBacklogRead;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationsReadThrough;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.NotificationFeed;

/// <summary>
/// #2130 - marking ONE followed-company notification read (ADR 0168), under EF InMemory: the tracked load and the
/// aggregate's own method, which is what the unit of work commits. What a mark is NOT is pinned here too: it moves no
/// watermark and touches no e-mail state. Every write on a match row, and the set-based marks ("Markera alla", the
/// switch-on backlog), are <c>ExecuteUpdate</c>, which InMemory does not run:
/// <c>NotificationFeedReadStateEndpointTests</c> proves them against Npgsql.
/// </summary>
public class NotificationFeedMarkerTests
{
    private static readonly DateTimeOffset T0 = NotificationFeedSeed.T0;
    private readonly NotificationFeedSeed _seed = new();

    private static NotificationFeedMarkContext Context(AppDbContext db, Guid userId) => new(db, userId);

    private static INotificationFeedReadMarker Marker(NotificationFeedSourceKind kind) =>
        NotificationFeedSources.MarkerFor(kind);

    [Fact]
    public async Task MarkRowAsync_OnAFollowedAd_MarksEveryHitOfThatAdForTheOwner_AndLeavesTheEmailStateAlone()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var seeker = _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, me, onlyMatched: false);
        var watchB = _seed.Watch(db, me, onlyMatched: false);
        var othersWatch = _seed.Watch(db, other, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        var elsewhere = _seed.ActiveAd(db);
        _seed.Hit(db, me, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, me, ad, watchB, T0.AddHours(2));
        _seed.Hit(db, me, elsewhere, watchA, T0.AddHours(3));
        _seed.Hit(db, other, ad, othersWatch, T0.AddHours(1));
        var watermark = seeker.LastSeenFollowedAdsAt;

        await Marker(NotificationFeedSourceKind.FollowedCompanyAd).MarkRowAsync(Context(db, me), ad, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.FollowedCompanyAdHits.Where(h => h.UserId == me && h.JobAdId == ad)
            .ShouldAllBe(h => h.ReadInNotificationFeed);
        db.FollowedCompanyAdHits.Single(h => h.JobAdId == elsewhere).ReadInNotificationFeed.ShouldBeFalse();
        db.FollowedCompanyAdHits.Single(h => h.UserId == other).ReadInNotificationFeed.ShouldBeFalse();
        // F3 (both ways): reading in the bell is not seeing the ad for the e-mail dedup, and moves no watermark.
        db.FollowedCompanyAdHits.ShouldAllBe(h => h.SeenAt == null && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending);
        db.JobSeekers.Single(js => js.UserId == me).LastSeenFollowedAdsAt.ShouldBe(watermark);
    }

    [Fact]
    public async Task MarkRowAsync_OnAFollowedAd_IsIdempotent_AndAMissingAdIsNoError()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watch, T0.AddHours(1));
        var marker = Marker(NotificationFeedSourceKind.FollowedCompanyAd);

        await marker.MarkRowAsync(Context(db, userId), ad, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await marker.MarkRowAsync(Context(db, userId), ad, CancellationToken.None);
        await marker.MarkRowAsync(Context(db, userId), new JobAdId(Guid.NewGuid()), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.FollowedCompanyAdHits.Single().ReadInNotificationFeed.ShouldBeTrue();
    }

    // #2151 (ADR 0169): the X on a followed ad dismisses every hit of the ad the owner has, read in the bell or not (the read
    // mark filters on the read flag; the X must not, or it does nothing on a row the user has opened), and touches no mail state.
    [Fact]
    public async Task DismissRowAsync_OnAFollowedAd_DismissesEveryHitOfThatAdForTheOwner_ReadOrNot_AndLeavesTheEmailStateAlone()
    {
        using var db = TestAppDbContextFactory.Create();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var seeker = _seed.Seeker(db, me, backgroundMatchConsent: false);
        _seed.Seeker(db, other, backgroundMatchConsent: false);
        var watchA = _seed.Watch(db, me, onlyMatched: false);
        var watchB = _seed.Watch(db, me, onlyMatched: false);
        var othersWatch = _seed.Watch(db, other, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        var elsewhere = _seed.ActiveAd(db);
        _seed.Hit(db, me, ad, watchA, T0.AddHours(1));
        _seed.Hit(db, me, ad, watchB, T0.AddHours(2));
        _seed.Hit(db, me, elsewhere, watchA, T0.AddHours(3));
        _seed.Hit(db, other, ad, othersWatch, T0.AddHours(1));
        NotificationFeedSeed.HitReadInBell(db, me, ad, watchA);
        var watermark = seeker.LastSeenFollowedAdsAt;

        await Marker(NotificationFeedSourceKind.FollowedCompanyAd).DismissRowAsync(Context(db, me), ad, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.FollowedCompanyAdHits.Where(h => h.UserId == me && h.JobAdId == ad)
            .ShouldAllBe(h => h.DismissedFromNotificationFeed && h.ReadInNotificationFeed);
        db.FollowedCompanyAdHits.Single(h => h.JobAdId == elsewhere).DismissedFromNotificationFeed.ShouldBeFalse();
        db.FollowedCompanyAdHits.Single(h => h.UserId == other).DismissedFromNotificationFeed.ShouldBeFalse();
        db.FollowedCompanyAdHits.ShouldAllBe(h => h.SeenAt == null && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending);
        db.JobSeekers.Single(js => js.UserId == me).LastSeenFollowedAdsAt.ShouldBe(watermark);
    }

    [Fact]
    public async Task DismissRowAsync_OnAFollowedAd_IsIdempotent_AndAMissingAdIsNoError()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: false);
        var watch = _seed.Watch(db, userId, onlyMatched: false);
        var ad = _seed.ActiveAd(db);
        _seed.Hit(db, userId, ad, watch, T0.AddHours(1));
        var marker = Marker(NotificationFeedSourceKind.FollowedCompanyAd);

        await marker.DismissRowAsync(Context(db, userId), ad, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await marker.DismissRowAsync(Context(db, userId), ad, CancellationToken.None);
        await marker.DismissRowAsync(Context(db, userId), new JobAdId(Guid.NewGuid()), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        db.FollowedCompanyAdHits.Single().DismissedFromNotificationFeed.ShouldBeTrue();
    }

    [Fact]
    public async Task TheDismissHandler_AnswersSuccessForARowThatIsNotThere_AndRefusesWithoutASessionUser()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        (await new DismissNotificationCommandHandler(db, currentUser).Handle(
            new DismissNotificationCommand(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid()), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        var anonymous = Substitute.For<ICurrentUser>();
        anonymous.UserId.Returns((Guid?)null);
        (await new DismissNotificationCommandHandler(db, anonymous).Handle(
            new DismissNotificationCommand(NotificationFeedSourceKind.Match, Guid.NewGuid()), CancellationToken.None))
            .Error.Code.ShouldBe("NotificationFeed.Unauthorized");
    }

    [Fact]
    public void TheDismissValidator_RefusesAnUndefinedSourceAndAnEmptyAd_FromTheRequestAlone()
    {
        new DismissNotificationCommandValidator()
            .Validate(new DismissNotificationCommand((NotificationFeedSourceKind)7, Guid.NewGuid())).IsValid.ShouldBeFalse();
        new DismissNotificationCommandValidator()
            .Validate(new DismissNotificationCommand(NotificationFeedSourceKind.Match, Guid.Empty)).IsValid.ShouldBeFalse();
        new DismissNotificationCommandValidator()
            .Validate(new DismissNotificationCommand(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid())).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task TheCommandHandlers_ResolveTheOwnerFromTheSessionAndAnswerSuccessForARowThatIsNotThere()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        _seed.Seeker(db, userId, backgroundMatchConsent: true);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        // A signed-in owner gets Success for every request: a row that is missing or foreign is not an error, so the answer
        // says nothing about whether the user has it. (The match row mark and the through marks are ExecuteUpdate, which
        // InMemory cannot run: their 204s are NotificationFeedReadStateEndpointTests'.)
        (await new MarkNotificationReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationReadCommand(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid()), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await new MarkNotificationFeedBacklogReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind.Match), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await new MarkNotificationFeedBacklogReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind.FollowedCompanyAd), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    // security V6, declared unreachable: no request carries a signed-in user without a JobSeeker row (registration creates
    // both; over HTTP such a user is a 401, measured), so this asserts only that the handlers degrade safely if the invariant
    // ever breaks: the same Success, never a NotFound that would say whether the user has a row. The match marks and the
    // through marks are ExecuteUpdate, which InMemory cannot run.
    [Fact]
    public async Task TheCommandHandlers_AnswerSuccessForASignedInUserWithoutAJobSeekerRow()
    {
        using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        (await new MarkNotificationReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationReadCommand(NotificationFeedSourceKind.FollowedCompanyAd, Guid.NewGuid()), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await new MarkNotificationFeedBacklogReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind.Match), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await new MarkNotificationFeedBacklogReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind.FollowedCompanyAd), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TheCommandHandlers_RefuseWithoutASessionUser_AndMarkNothing()
    {
        using var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        (await new MarkNotificationReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationReadCommand(NotificationFeedSourceKind.Match, Guid.NewGuid()), CancellationToken.None))
            .Error.Code.ShouldBe("NotificationFeed.Unauthorized");
        (await new MarkNotificationsReadThroughCommandHandler(db, currentUser).Handle(
            new MarkNotificationsReadThroughCommand(NotificationFeedSourceKind.Match, T0), CancellationToken.None))
            .Error.Code.ShouldBe("NotificationFeed.Unauthorized");
        (await new MarkNotificationFeedBacklogReadCommandHandler(db, currentUser).Handle(
            new MarkNotificationFeedBacklogReadCommand(NotificationFeedSourceKind.Match), CancellationToken.None))
            .Error.Code.ShouldBe("NotificationFeed.Unauthorized");
    }

    [Fact]
    public void TheValidators_RefuseAnUndefinedSourceAnEmptyAdAndAnUnboundToken_FromTheRequestAlone()
    {
        const NotificationFeedSourceKind undefined = (NotificationFeedSourceKind)7;

        new MarkNotificationReadCommandValidator()
            .Validate(new MarkNotificationReadCommand(undefined, Guid.NewGuid())).IsValid.ShouldBeFalse();
        new MarkNotificationReadCommandValidator()
            .Validate(new MarkNotificationReadCommand(NotificationFeedSourceKind.Match, Guid.Empty)).IsValid.ShouldBeFalse();
        new MarkNotificationsReadThroughCommandValidator()
            .Validate(new MarkNotificationsReadThroughCommand(undefined, T0)).IsValid.ShouldBeFalse();
        new MarkNotificationsReadThroughCommandValidator()
            .Validate(new MarkNotificationsReadThroughCommand(NotificationFeedSourceKind.Match, default)).IsValid.ShouldBeFalse();
        new MarkNotificationFeedBacklogReadCommandValidator()
            .Validate(new MarkNotificationFeedBacklogReadCommand(undefined)).IsValid.ShouldBeFalse();

        new MarkNotificationReadCommandValidator()
            .Validate(new MarkNotificationReadCommand(NotificationFeedSourceKind.Match, Guid.NewGuid())).IsValid.ShouldBeTrue();
        new MarkNotificationsReadThroughCommandValidator()
            .Validate(new MarkNotificationsReadThroughCommand(NotificationFeedSourceKind.FollowedCompanyAd, T0)).IsValid.ShouldBeTrue();
    }
}
