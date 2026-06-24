using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.UpdateNotificationConsent;

// ADR 0080 Vag 4 PR-6 (Beslut 2) — the background-match notification consent write-path
// handler. Mirrors the owner-scoped audited DeleteAccount shape: current-user gate
// (UserId.HasValue), owner-scoped TRACKED load of the JobSeeker via UserId, then a thin
// delegation to the aggregate (jobSeeker.UpdateNotificationConsent) which owns the GDPR consent
// stamping. SaveChanges is the UnitOfWorkBehavior's job — assertions read the tracked entity.
//
// Audit contract (security-auditor Major, ADR 0022): the command is IAuditableCommand and
// returns Result<Guid> echoing the JobSeeker's Id, so AuditBehavior.ExtractAggregateId(response)
// reads it for the audit_log AggregateId. So a success here asserts BOTH success AND
// result.Value == jobSeeker.Id.Value (the echoed id that feeds the audit row). The audit row
// itself is written by AuditBehavior (a pipeline concern), proven at the integration level
// (NotificationConsentEndpointTests) where the behavior actually runs.
//
// Typed-error contract (never Swedish message text — localization-fragile): unauthenticated
// → Result.Failure<Guid> code "JobSeeker.Unauthorized"; no JobSeeker → DomainError.NotFound code
// "JobSeeker.NotFound".
//
// GDPR invariants pinned here (Art. 7): the first-ever opt-in stamps NotificationConsentAt ONCE
// and it is IMMUTABLE evidence (a re-enable after withdrawal must NOT move it); an opt-out from
// an enabled state stamps NotificationConsentWithdrawnAt (revocation proof).
public class UpdateNotificationConsentCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // Distinct, ordered instants so "did the timestamp move?" is observable.
    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero));

    public UpdateNotificationConsentCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", ClockT0).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private UpdateNotificationConsentCommandHandler HandlerWith(
        AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new UpdateNotificationConsentCommandHandler(db, currentUser, ClockT0);

        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Weekly),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = HandlerWith(db, ClockT0);

        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Weekly),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WhenEnabling_StampsConsentAndSetsCadence_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var handler = HandlerWith(db, ClockT1);

        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Daily),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The echoed id feeds the audit_log AggregateId (AuditBehavior.ExtractAggregateId).
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        // Art. 7(1) — the opt-in evidence is stamped at the consent instant.
        prefs.NotificationConsentAt.ShouldBe(ClockT1.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenDisablingFromEnabled_StampsWithdrawal_AndKeepsConsentAt()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Pre-state: enabled at T1 (consent stamped).
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: opt-out at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: false, Cadence: DigestCadence.Weekly),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        // Art. 7(3) — the revocation time is recorded at the opt-out instant.
        prefs.NotificationConsentWithdrawnAt.ShouldBe(ClockT2.UtcNow);
        // The original opt-in evidence is preserved (Art. 7(1) — immutable).
        prefs.NotificationConsentAt.ShouldBe(ClockT1.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenReEnablingAfterWithdrawal_ClearsWithdrawal_AndDoesNotMoveConsentAt()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Enable at T0 (first opt-in evidence) → disable at T1 (withdrawal).
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, ClockT0);
        seeker.UpdateNotificationConsent(enabled: false, DigestCadence.Weekly, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: re-enable at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Daily),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        // The IMMUTABLE first-opt-in evidence must NOT move on re-consent (Art. 7(1)).
        prefs.NotificationConsentAt.ShouldBe(ClockT0.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenChangingCadenceWhileEnabled_UpdatesCadence_AndLeavesTimestampsUnchanged()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Weekly, ClockT0);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: still enabled, only the cadence flips Weekly → Daily at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Daily),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        // A cadence-only change does not re-stamp consent nor introduce a withdrawal.
        prefs.NotificationConsentAt.ShouldBe(ClockT0.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersConsent()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);
        var handler = HandlerWith(db, ClockT1);

        var result = await handler.Handle(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: DigestCadence.Daily),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The echoed audit id is the ACTING user's JobSeeker, never the other user's.
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        var untouched = db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences;
        // Default OFF (opt-in) — the other user's consent must remain untouched.
        untouched.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
        untouched.NotificationConsentAt.ShouldBeNull();
        otherSeeker.Id.ShouldBe(db.JobSeekers.Single(js => js.UserId == otherUserId).Id);
    }
}
