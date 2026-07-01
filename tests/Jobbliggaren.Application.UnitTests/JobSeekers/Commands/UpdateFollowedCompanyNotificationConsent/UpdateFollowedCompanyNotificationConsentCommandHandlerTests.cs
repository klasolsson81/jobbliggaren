using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateFollowedCompanyNotificationConsent;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.UpdateFollowedCompanyNotificationConsent;

// ADR 0087 D3/D5 (#311 PR-2b) — the company-follow notification consent write-path handler.
// Mirrors the owner-scoped audited UpdateNotificationConsent shape: current-user gate
// (UserId.HasValue), owner-scoped TRACKED load of the JobSeeker via UserId, then a thin delegation
// to the aggregate (jobSeeker.UpdateFollowedCompanyNotificationConsent, shipped PR-4) which owns
// the GDPR consent stamping. SaveChanges is the UnitOfWorkBehavior's job — assertions read the
// tracked entity.
//
// Audit contract (ADR 0022): the command is IAuditableCommand and returns Result<Guid> echoing the
// JobSeeker's Id, so AuditBehavior.ExtractAggregateId(response) reads it for the audit_log
// AggregateId. So a success here asserts BOTH success AND result.Value == jobSeeker.Id.Value. The
// audit row itself is written by AuditBehavior (a pipeline concern), proven at the integration
// level (FollowedCompanyNotificationConsentEndpointTests) where the behavior actually runs.
//
// Typed-error contract (never Swedish message text — localization-fragile): unauthenticated →
// Result.Failure<Guid> code "JobSeeker.Unauthorized"; no JobSeeker → DomainError.NotFound code
// "JobSeeker.NotFound".
//
// GDPR invariants pinned here (Art. 7): the first-ever opt-in stamps
// FollowedCompanyNotificationConsentAt ONCE and it is IMMUTABLE evidence (a re-enable after
// withdrawal must NOT move it); an opt-out from an enabled state stamps
// FollowedCompanyNotificationConsentWithdrawnAt (revocation proof). SEPARATENESS (ADR 0087 D5):
// toggling the follow consent must NEVER touch the background-match consent flag/timestamps — the
// two are distinct Art. 6/7 processing purposes.
public class UpdateFollowedCompanyNotificationConsentCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // Distinct, ordered instants so "did the timestamp move?" is observable.
    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));

    public UpdateFollowedCompanyNotificationConsentCommandHandlerTests()
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

    private UpdateFollowedCompanyNotificationConsentCommandHandler HandlerWith(
        AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new UpdateFollowedCompanyNotificationConsentCommandHandler(db, currentUser, ClockT0);

        var result = await handler.Handle(
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
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
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WhenEnabling_StampsConsent_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var handler = HandlerWith(db, ClockT1);

        var result = await handler.Handle(
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The echoed id feeds the audit_log AggregateId (AuditBehavior.ExtractAggregateId).
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        // Art. 7(1) — the opt-in evidence is stamped at the consent instant.
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(ClockT1.UtcNow);
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenDisablingFromEnabled_StampsWithdrawal_AndKeepsConsentAt()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Pre-state: enabled at T1 (consent stamped).
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: opt-out at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: false),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        // Art. 7(3) — the revocation time is recorded at the opt-out instant.
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBe(ClockT2.UtcNow);
        // The original opt-in evidence is preserved (Art. 7(1) — immutable).
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(ClockT1.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenReEnablingAfterWithdrawal_ClearsWithdrawal_AndDoesNotMoveConsentAt()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Enable at T0 (first opt-in evidence) → disable at T1 (withdrawal).
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: true, ClockT0);
        seeker.UpdateFollowedCompanyNotificationConsent(enabled: false, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: re-enable at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationConsentWithdrawnAt.ShouldBeNull();
        // The IMMUTABLE first-opt-in evidence must NOT move on re-consent (Art. 7(1)).
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(ClockT0.UtcNow);
    }

    // ADR 0087 D5 — the two consents are SEPARATE processing purposes. Toggling the company-follow
    // consent must never touch the background-match consent flag or its Art. 7 timestamps (and the
    // shared cadence is not this command's concern — it carries no cadence field).
    [Fact]
    public async Task Handle_WhenEnablingFollow_DoesNotTouchBackgroundMatchConsent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        // Pre-state: background-match consent enabled at T0 with a chosen cadence.
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, ClockT0);
        await db.SaveChangesAsync(CancellationToken.None);

        // Act: enable the follow consent at T2.
        var handler = HandlerWith(db, ClockT2);
        var result = await handler.Handle(
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var prefs = db.JobSeekers.Single(js => js.UserId == _userId).Preferences;
        // Follow consent is now on with its own evidence.
        prefs.FollowedCompanyNotificationsEnabled.ShouldBeTrue();
        prefs.FollowedCompanyNotificationConsentAt.ShouldBe(ClockT2.UtcNow);
        // Background-match consent + its evidence + the shared cadence are UNTOUCHED.
        prefs.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        prefs.NotificationConsentAt.ShouldBe(ClockT0.UtcNow);
        prefs.NotificationConsentWithdrawnAt.ShouldBeNull();
        prefs.DigestCadence.ShouldBe(DigestCadence.Daily);
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
            new UpdateFollowedCompanyNotificationConsentCommand(Enabled: true),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The echoed audit id is the ACTING user's JobSeeker, never the other user's.
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        var untouched = db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences;
        // Default OFF (opt-in) — the other user's follow consent must remain untouched.
        untouched.FollowedCompanyNotificationsEnabled.ShouldBeFalse();
        untouched.FollowedCompanyNotificationConsentAt.ShouldBeNull();
    }
}
