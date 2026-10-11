using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobSeekers.Commands.SetNotificationMail;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetNotificationMail;

// ADR 0164 (#2107) — the all-notification-mail switch. Same owner-scoped audited shape as the consent
// handlers; SaveChanges is UnitOfWorkBehavior's, so assertions read the tracked entity. The switch is a
// pause inside the consents: it never writes a consent field (security-auditor V3).
public class SetNotificationMailCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero));

    public SetNotificationMailCommandHandlerTests()
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

    private SetNotificationMailCommandHandler HandlerWith(AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    private Preferences StoredPreferences(AppDbContext db) =>
        db.JobSeekers.Single(js => js.UserId == _userId).Preferences;

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new SetNotificationMailCommandHandler(db, currentUser, ClockT0);

        var result = await handler.Handle(new SetNotificationMailCommand(Enabled: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await HandlerWith(db, ClockT0).Handle(
            new SetNotificationMailCommand(Enabled: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WhenSwitchingOff_StampsThePause_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetNotificationMailCommand(Enabled: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = StoredPreferences(db);
        prefs.NotificationMailOffAt.ShouldBe(ClockT1.UtcNow);
        prefs.TopMatchMailOffAt.ShouldBeNull();
        prefs.MatchSummaryMailOffAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenSwitchingOnAfterOff_ClearsThePause_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.SetNotificationMail(on: false, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await HandlerWith(db, ClockT2).Handle(
            new SetNotificationMailCommand(Enabled: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        StoredPreferences(db).NotificationMailOffAt.ShouldBeNull();
    }

    // security-auditor V3: switching every mail on is never a consent, and switching it off is never a
    // withdrawal. Both consents are read in the two states the aggregate produces for them here: the
    // background-match consent withdrawn or granted, the follow consent granted.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Handle_NeverChangesAConsentField(bool enabled, bool backgroundMatchWithdrawn)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        if (backgroundMatchWithdrawn)
            seeker.UpdateNotificationConsent(enabled: false, ClockT1);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, ClockT0);
        seeker.SetNotificationMail(on: !enabled, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = StoredPreferences(db);

        var result = await HandlerWith(db, ClockT2).Handle(
            new SetNotificationMailCommand(Enabled: enabled), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var after = StoredPreferences(db);
        after.NotificationMailOffAt.ShouldBe(enabled ? null : ClockT2.UtcNow);
        after.ShouldBe(before with { NotificationMailOffAt = after.NotificationMailOffAt });
        after.BackgroundMatchNotificationsEnabled.ShouldBe(!backgroundMatchWithdrawn);
        after.NotificationConsentAt.ShouldBe(ClockT0.UtcNow);
        after.NotificationConsentWithdrawnAt.ShouldBe(backgroundMatchWithdrawn ? ClockT1.UtcNow : null);
        after.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        after.FollowedCompanyNotificationConsentAt.ShouldBe(ClockT0.UtcNow);
        after.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(true, "{\"enabled\":true}")]
    [InlineData(false, "{\"enabled\":false}")]
    public void BuildAuditPayload_IsExactlyTheRequestedEnabled(bool enabled, string expected)
    {
        var payload = new SetNotificationMailCommand(Enabled: enabled).BuildAuditPayload(
            Result.Success(Guid.NewGuid()), Substitute.For<IIdentifierPseudonymizer>());

        payload.ShouldBe(expected);
    }

    [Fact]
    public void Command_IsAuditedAsNotificationMailUpdatedOnTheJobSeeker()
    {
        var command = new SetNotificationMailCommand(Enabled: false);

        command.EventType.ShouldBe("JobSeeker.NotificationMailUpdated");
        command.AggregateType.ShouldBe("JobSeeker");
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersSwitch()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetNotificationMailCommand(Enabled: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences.NotificationMailOffAt.ShouldBeNull();
    }
}
