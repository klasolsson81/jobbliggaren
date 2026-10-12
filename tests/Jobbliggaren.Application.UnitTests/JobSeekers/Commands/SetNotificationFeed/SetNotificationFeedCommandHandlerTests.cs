using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedFollowedAds;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedMatches;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetNotificationFeed;

// ADR 0162 (#2130) - the two bell switches. Same owner-scoped audited shape as the mail pauses (ADR 0164); SaveChanges is
// UnitOfWorkBehavior's, so assertions read the tracked entity. A bell switch writes its own stamp and nothing else.
public class SetNotificationFeedCommandHandlerTests
{
    public enum Switch
    {
        Matches,
        FollowedAds,
    }

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero));

    public SetNotificationFeedCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(ClockT0), ClockT0).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static ValueTask<Result<Guid>> Handle(
        Switch which, AppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, bool enabled) => which switch
        {
            Switch.Matches => new SetNotificationFeedMatchesCommandHandler(db, currentUser, clock)
                .Handle(new SetNotificationFeedMatchesCommand(enabled), CancellationToken.None),
            Switch.FollowedAds => new SetNotificationFeedFollowedAdsCommandHandler(db, currentUser, clock)
                .Handle(new SetNotificationFeedFollowedAdsCommand(enabled), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
        };

    private static DateTimeOffset? Stamp(Preferences preferences, Switch which) => which switch
    {
        Switch.Matches => preferences.NotificationFeedMatchesOffAt,
        Switch.FollowedAds => preferences.NotificationFeedFollowedAdsOffAt,
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
    };

    private static Preferences WithStamp(Preferences preferences, Switch which, DateTimeOffset? stamp) => which switch
    {
        Switch.Matches => preferences with { NotificationFeedMatchesOffAt = stamp },
        Switch.FollowedAds => preferences with { NotificationFeedFollowedAdsOffAt = stamp },
        _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
    };

    private Preferences StoredPreferences(AppDbContext db) =>
        db.JobSeekers.Single(js => js.UserId == _userId).Preferences;

    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure(Switch which)
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var result = await Handle(which, db, currentUser, ClockT0, enabled: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound(Switch which)
    {
        var db = TestAppDbContextFactory.Create();

        var result = await Handle(which, db, _currentUser, ClockT0, enabled: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // Everything else the user owns is set first (both consents, a mail pause, the other bell switch), so the assertion
    // sees that only this switch's stamp moved (security V3).
    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_WhenSwitchingOff_StampsOnlyItsOwnSwitch_AndEchoesJobSeekerId(Switch which)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, ClockT0);
        seeker.SetTopMatchMail(on: false, ClockT0);
        if (which == Switch.Matches)
            seeker.SetNotificationFeedFollowedAds(on: false, ClockT0);
        else
            seeker.SetNotificationFeedMatches(on: false, ClockT0);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = StoredPreferences(db);

        var result = await Handle(which, db, _currentUser, ClockT1, enabled: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        StoredPreferences(db).ShouldBe(WithStamp(before, which, ClockT1.UtcNow));
    }

    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_WhenSwitchingOnAfterOff_ClearsOnlyItsOwnSwitch_AndEchoesJobSeekerId(Switch which)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.SetNotificationMail(on: false, ClockT0);
        if (which == Switch.Matches)
            seeker.SetNotificationFeedMatches(on: false, ClockT1);
        else
            seeker.SetNotificationFeedFollowedAds(on: false, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = StoredPreferences(db);

        var result = await Handle(which, db, _currentUser, ClockT2, enabled: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        StoredPreferences(db).ShouldBe(WithStamp(before, which, null));
        Stamp(StoredPreferences(db), which).ShouldBeNull();
    }

    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_NeverChangesAConsentField_InEitherDirection(Switch which)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, ClockT0);
        await db.SaveChangesAsync(CancellationToken.None);
        var consents = StoredPreferences(db);

        await Handle(which, db, _currentUser, ClockT1, enabled: false);
        await Handle(which, db, _currentUser, ClockT2, enabled: true);

        var after = StoredPreferences(db);
        after.BackgroundMatchNotificationsEnabled.ShouldBe(consents.BackgroundMatchNotificationsEnabled);
        after.NotificationConsentAt.ShouldBe(consents.NotificationConsentAt);
        after.NotificationConsentWithdrawnAt.ShouldBe(consents.NotificationConsentWithdrawnAt);
        after.FollowedCompanyNotificationsEnabled.ShouldBe(consents.FollowedCompanyNotificationsEnabled);
        after.FollowedCompanyNotificationConsentAt.ShouldBe(consents.FollowedCompanyNotificationConsentAt);
        after.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBe(consents.FollowedCompanyNotificationConsentWithdrawnAt);
    }

    [Theory]
    [InlineData(Switch.Matches, true, "{\"enabled\":true}")]
    [InlineData(Switch.Matches, false, "{\"enabled\":false}")]
    [InlineData(Switch.FollowedAds, true, "{\"enabled\":true}")]
    [InlineData(Switch.FollowedAds, false, "{\"enabled\":false}")]
    public void BuildAuditPayload_IsExactlyTheRequestedEnabled(Switch which, bool enabled, string expected)
    {
        var pseudonymizer = Substitute.For<IIdentifierPseudonymizer>();
        var payload = which == Switch.Matches
            ? new SetNotificationFeedMatchesCommand(enabled).BuildAuditPayload(Result.Success(Guid.NewGuid()), pseudonymizer)
            : new SetNotificationFeedFollowedAdsCommand(enabled).BuildAuditPayload(Result.Success(Guid.NewGuid()), pseudonymizer);

        payload.ShouldBe(expected);
    }

    [Fact]
    public void Commands_AreAuditedUnderTheirOwnStableEventNamesOnTheJobSeeker()
    {
        var matches = new SetNotificationFeedMatchesCommand(Enabled: false);
        var followed = new SetNotificationFeedFollowedAdsCommand(Enabled: false);

        matches.EventType.ShouldBe("JobSeeker.NotificationFeedMatchesUpdated");
        followed.EventType.ShouldBe("JobSeeker.NotificationFeedFollowedAdsUpdated");
        matches.AggregateType.ShouldBe("JobSeeker");
        followed.AggregateType.ShouldBe("JobSeeker");
    }

    [Theory]
    [InlineData(Switch.Matches)]
    [InlineData(Switch.FollowedAds)]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersSwitch(Switch which)
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);

        var result = await Handle(which, db, _currentUser, ClockT1, enabled: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        Stamp(db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences, which).ShouldBeNull();
    }
}
