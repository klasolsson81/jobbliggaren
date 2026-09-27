using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.SetDigestCadence;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetDigestCadence;

// ADR 0087 D2 — the digest-cadence write path. Same owner-scoped audited shape as the consent
// handlers: current-user gate, owner-scoped TRACKED load, delegation to the aggregate, and the
// JobSeeker id echoed for the audit row (AuditBehavior.ExtractAggregateId). SaveChanges is the
// UnitOfWorkBehavior's job — assertions read the tracked entity.
public class SetDigestCadenceCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));

    public SetDigestCadenceCommandHandlerTests()
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

    private SetDigestCadenceCommandHandler HandlerWith(AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new SetDigestCadenceCommandHandler(db, currentUser, ClockT0);

        var result = await handler.Handle(
            new SetDigestCadenceCommand(DigestCadence.Daily), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await HandlerWith(db, ClockT0).Handle(
            new SetDigestCadenceCommand(DigestCadence.Daily), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_SetsTheCadence_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetDigestCadenceCommand(DigestCadence.Daily), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The echoed id feeds the audit_log AggregateId (AuditBehavior.ExtractAggregateId).
        result.Value.ShouldBe(seeker.Id.Value);
        db.JobSeekers.Single(js => js.UserId == _userId).Preferences.DigestCadence
            .ShouldBe(DigestCadence.Daily);
    }

    [Fact]
    public async Task Handle_AfterWithdrawal_LeavesTheConsentAndBothArt7TimestampsUnchanged()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.UpdateNotificationConsent(enabled: false, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await HandlerWith(db, ClockT2).Handle(
            new SetDigestCadenceCommand(DigestCadence.Daily), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        prefs.NotificationConsentAt.ShouldBe(ClockT0.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBe(ClockT1.UtcNow);
    }

    [Fact]
    public async Task Handle_WithUndefinedCadence_ReturnsTheAggregatesValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetDigestCadenceCommand((DigestCadence)99), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DigestCadenceInvalid");
        db.JobSeekers.Single(js => js.UserId == _userId).Preferences.DigestCadence
            .ShouldBe(DigestCadence.Weekly);
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersCadence()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetDigestCadenceCommand(DigestCadence.Daily), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences.DigestCadence
            .ShouldBe(DigestCadence.Weekly);
    }
}
