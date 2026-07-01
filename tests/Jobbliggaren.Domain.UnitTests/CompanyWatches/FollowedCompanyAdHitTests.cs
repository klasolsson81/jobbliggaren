using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — invariants for the <see cref="FollowedCompanyAdHit"/> aggregate root:
/// the <see cref="FollowedCompanyAdHit.Create"/> guards, the one-way notification state machine
/// (Pending → Queued → Sent), and idempotent soft-delete. The UNIQUE dedup + Art. 17 cascade wiring
/// are pinned by the EF config + AccountHardDeleteCascadeFitnessTests /
/// HardDeleteAccountsJobIntegrationTests. The no-grade/no-score posture (a company-follow hit is not
/// scored) is structural — the type simply has no grade field.
/// </summary>
public class FollowedCompanyAdHitTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();
    private static readonly JobAdId ValidJobAdId = JobAdId.New();
    private static readonly CompanyWatchId ValidWatchId = CompanyWatchId.New();

    private static FollowedCompanyAdHit CreateValid() =>
        FollowedCompanyAdHit.Create(ValidUserId, ValidJobAdId, ValidWatchId, Clock).Value;

    // ---------------------------------------------------------------
    // Create — happy path + guards
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_CreatesPendingHit()
    {
        var result = FollowedCompanyAdHit.Create(ValidUserId, ValidJobAdId, ValidWatchId, Clock);

        result.IsSuccess.ShouldBeTrue();
        var hit = result.Value;
        hit.UserId.ShouldBe(ValidUserId);
        hit.JobAdId.ShouldBe(ValidJobAdId);
        hit.CompanyWatchId.ShouldBe(ValidWatchId);
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        hit.CreatedAt.ShouldBe(Clock.UtcNow);
        hit.SentAt.ShouldBeNull();
        hit.DeletedAt.ShouldBeNull();
        hit.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithEmptyUserId_Fails()
    {
        var result = FollowedCompanyAdHit.Create(Guid.Empty, ValidJobAdId, ValidWatchId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.UserIdRequired");
    }

    [Fact]
    public void Create_WithDefaultJobAdId_Fails()
    {
        var result = FollowedCompanyAdHit.Create(ValidUserId, default, ValidWatchId, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.JobAdIdRequired");
    }

    [Fact]
    public void Create_WithDefaultCompanyWatchId_Fails()
    {
        var result = FollowedCompanyAdHit.Create(ValidUserId, ValidJobAdId, default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.CompanyWatchIdRequired");
    }

    // ---------------------------------------------------------------
    // State machine — Pending → Queued → Sent (one-way, guarded)
    // ---------------------------------------------------------------

    [Fact]
    public void MarkQueued_FromPending_TransitionsToQueued()
    {
        var hit = CreateValid();

        var result = hit.MarkQueued();

        result.IsSuccess.ShouldBeTrue();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
    }

    [Fact]
    public void MarkQueued_WhenAlreadyQueued_Fails()
    {
        // Idempotency spine: a re-scan/re-dispatch that finds an already-Queued row must not
        // re-queue it (no re-notification). Only a Pending hit can be queued.
        var hit = CreateValid();
        hit.MarkQueued();

        var result = hit.MarkQueued();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotPending");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
    }

    [Fact]
    public void MarkSent_FromPending_Fails()
    {
        // Cannot send before claiming (Queued) — the claim-then-send idempotency spine.
        var hit = CreateValid();

        var result = hit.MarkSent(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotQueued");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        hit.SentAt.ShouldBeNull();
    }

    [Fact]
    public void MarkSent_FromQueued_TransitionsToSent_AndStampsSentAt()
    {
        var hit = CreateValid();
        hit.MarkQueued();
        var sentClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5));

        var result = hit.MarkSent(sentClock);

        result.IsSuccess.ShouldBeTrue();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
        hit.SentAt.ShouldBe(sentClock.UtcNow);
    }

    [Fact]
    public void MarkSent_WhenAlreadySent_Fails_AndKeepsFirstSentAt()
    {
        // Never re-send: the SentAt stamp is written once (the "never double-email" stance).
        var hit = CreateValid();
        hit.MarkQueued();
        var firstSentClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5));
        hit.MarkSent(firstSentClock);

        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(30));
        var result = hit.MarkSent(laterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotQueued");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
        hit.SentAt.ShouldBe(firstSentClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // SoftDelete — idempotent (Art. 17 cascade join by UserId)
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_OnActiveHit_StampsDeletedAt()
    {
        var hit = CreateValid();
        var deleteClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));

        hit.SoftDelete(deleteClock);

        hit.DeletedAt.ShouldBe(deleteClock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotent()
    {
        var hit = CreateValid();
        var firstDeleteClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(2));
        hit.SoftDelete(firstDeleteClock);

        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddDays(5));
        hit.SoftDelete(laterClock);

        hit.DeletedAt.ShouldBe(firstDeleteClock.UtcNow);
    }
}
