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

    // SoftDelete + DeletedAt retired by #868 (writerless decoy axis, same disease as #821/#915) —
    // Art. 17 erasure is a HARD delete via AccountHardDeleter. No soft-delete tests remain.

    // ---------------------------------------------------------------
    // MarkSeen - #453 cross-channel dedup (Pending-only, idempotent)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_LeavesSeenAtNull()
    {
        // #453 - a fresh hit has never been opened in-app; the digest may still email it.
        var hit = CreateValid();

        hit.SeenAt.ShouldBeNull();
    }

    [Fact]
    public void MarkSeen_FromPending_StampsSeenAtToClockNow()
    {
        // The user opened this ad in-app while the hit was still Pending -> the follow-digest suppresses
        // the redundant email ("aldrig mejla nagot jag sett i appen").
        var hit = CreateValid();
        var seenClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10));

        var result = hit.MarkSeen(seenClock);

        result.IsSuccess.ShouldBeTrue();
        hit.SeenAt.ShouldNotBeNull();
        hit.SeenAt!.Value.ShouldBe(seenClock.UtcNow);
        // Status is untouched - MarkSeen only stamps, it does not advance the notification machine.
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
    }

    [Fact]
    public void MarkSeen_FromQueued_IsNoOpSuccess_AndLeavesSeenAtNull()
    {
        // Post-claim (Queued): the email decision is already taken, so MarkSeen is a deliberate no-op
        // (Success, not a failure) and does NOT stamp SeenAt - the Sent-dedup prevents repetition.
        var hit = CreateValid();
        hit.MarkQueued();

        var result = hit.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));

        result.IsSuccess.ShouldBeTrue();
        hit.SeenAt.ShouldBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Queued);
    }

    [Fact]
    public void MarkSeen_FromSent_IsNoOpSuccess_AndLeavesSeenAtNull()
    {
        // Post-send (Sent): same deliberate no-op - the email already went out.
        var hit = CreateValid();
        hit.MarkQueued();
        hit.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));

        var result = hit.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));

        result.IsSuccess.ShouldBeTrue();
        hit.SeenAt.ShouldBeNull();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    [Fact]
    public void MarkSeen_WhenAlreadySeen_IsIdempotent_AndKeepsFirstStamp()
    {
        // First-seen wins: a re-open leaves the first stamp intact (the invariant is only SeenAt != null).
        var hit = CreateValid();
        var firstSeenClock = FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10));
        hit.MarkSeen(firstSeenClock);

        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));
        var result = hit.MarkSeen(laterClock);

        result.IsSuccess.ShouldBeTrue();
        hit.SeenAt!.Value.ShouldBe(firstSeenClock.UtcNow, "first-seen wins - a re-open never overwrites the stamp");
    }

    [Fact]
    public void MarkSeen_OnPendingHit_YieldsStateThatDigestSuppressPredicateExcludes()
    {
        // State-machine cross-check: the follow-digest due-set is `Pending AND SeenAt == null`. A hit
        // that has been MarkSeen'd while Pending has SeenAt != null AND status == Pending, so that
        // predicate excludes it (it is never claimed -> never emailed -> falls dormant).
        var hit = CreateValid();

        hit.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));

        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        hit.SeenAt.ShouldNotBeNull();
        // The digest's `Pending && SeenAt == null` gate is false for this hit.
        (hit.NotificationStatus == FollowedCompanyAdHitStatus.Pending && hit.SeenAt is null)
            .ShouldBeFalse("a seen-in-app Pending hit must be excluded from the follow-digest due-set");
    }

    // ---------------------------------------------------------------
    // MarkFailed - #453 audit #26 stranded-Queued recovery (terminal)
    // ---------------------------------------------------------------

    [Fact]
    public void MarkFailed_FromQueued_TransitionsToFailed()
    {
        // The reaper's follow arm moves a long-stranded Queued row to terminal Failed.
        var hit = CreateValid();
        hit.MarkQueued();

        var result = hit.MarkFailed();

        result.IsSuccess.ShouldBeTrue();
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    [Fact]
    public void MarkFailed_FromPending_Fails()
    {
        // A Pending row was never claimed - it is not reapable.
        var hit = CreateValid();

        var result = hit.MarkFailed();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotQueued");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
    }

    [Fact]
    public void MarkFailed_FromSent_Fails()
    {
        // A Sent row already delivered - never reaped.
        var hit = CreateValid();
        hit.MarkQueued();
        hit.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));

        var result = hit.MarkFailed();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotQueued");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    [Fact]
    public void MarkFailed_WhenAlreadyFailed_Fails_Terminal()
    {
        // Failed is terminal - there is no transition out of it.
        var hit = CreateValid();
        hit.MarkQueued();
        hit.MarkFailed();

        var result = hit.MarkFailed();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowedCompanyAdHit.NotQueued");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Failed);
    }

    // ---------------------------------------------------------------
    // ReadInNotificationFeed - #2130, the bell's own per-row read state. NOT SeenAt: SeenAt is the
    // e-mail dedup authority ("aldrig mejla något jag sett i appen"), set when the AD is opened anywhere
    // in-app and only on a Pending hit. The two answer different questions and never write each other.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_StartsUnreadInTheNotificationFeed()
    {
        CreateValid().ReadInNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public void MarkReadInNotificationFeed_SetsTheFlag_AndTouchesNoMailState()
    {
        var hit = CreateValid();

        hit.MarkReadInNotificationFeed();

        hit.ReadInNotificationFeed.ShouldBeTrue();
        hit.SeenAt.ShouldBeNull("reading in the bell must not suppress the follow e-mail");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        hit.SentAt.ShouldBeNull();
        hit.CreatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void MarkReadInNotificationFeed_WhenAlreadyRead_IsIdempotent()
    {
        var hit = CreateValid();
        hit.MarkReadInNotificationFeed();

        hit.MarkReadInNotificationFeed();

        hit.ReadInNotificationFeed.ShouldBeTrue();
    }

    [Fact]
    public void MarkReadInNotificationFeed_WorksAfterTheMailIsClaimedOrSent()
    {
        // Unlike MarkSeen (Pending-only), the bell's read mark has no mail precondition: a notification
        // that was mailed is still a notification the user can open in the bell.
        var queued = CreateValid();
        queued.MarkQueued();
        queued.MarkReadInNotificationFeed();
        queued.ReadInNotificationFeed.ShouldBeTrue();

        var sent = CreateValid();
        sent.MarkQueued();
        sent.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));
        sent.MarkReadInNotificationFeed();
        sent.ReadInNotificationFeed.ShouldBeTrue();
        sent.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    [Fact]
    public void MarkSeen_NeverSetsTheBellsReadFlag()
    {
        var hit = CreateValid();

        hit.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));

        hit.SeenAt.ShouldNotBeNull();
        hit.ReadInNotificationFeed.ShouldBeFalse("opening the ad is not reading the notification (B12)");
    }

    [Fact]
    public void TheMailStateMachine_NeverChangesTheBellsReadFlag()
    {
        var unread = CreateValid();
        unread.MarkQueued();
        unread.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));
        unread.ReadInNotificationFeed.ShouldBeFalse();

        var failed = CreateValid();
        failed.MarkQueued();
        failed.MarkFailed();
        failed.ReadInNotificationFeed.ShouldBeFalse();

        var read = CreateValid();
        read.MarkReadInNotificationFeed();
        read.MarkQueued();
        read.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));
        read.ReadInNotificationFeed.ShouldBeTrue("the mail path never clears a read mark");
    }

    // ---------------------------------------------------------------
    // DismissedFromNotificationFeed - #2151 (ADR 0169), the X on a bell notification. A dismissed hit is a read hit, so
    // the bell's count and token need no knowledge of it; the mail state machine and the other bell writers never touch it.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_StartsNotDismissed()
    {
        CreateValid().DismissedFromNotificationFeed.ShouldBeFalse();
    }

    [Fact]
    public void DismissFromNotificationFeed_SetsBothFlags_AndTouchesNoMailState()
    {
        var hit = CreateValid();

        hit.DismissFromNotificationFeed();

        hit.DismissedFromNotificationFeed.ShouldBeTrue();
        hit.ReadInNotificationFeed.ShouldBeTrue("a dismissed hit is a read hit (the CHECK holds it in the database)");
        hit.SeenAt.ShouldBeNull("dismissing in the bell must not suppress the follow e-mail");
        hit.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Pending);
        hit.SentAt.ShouldBeNull();
        hit.CreatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void DismissFromNotificationFeed_IsIdempotent_AndWorksOnAHitAlreadyRead()
    {
        var read = CreateValid();
        read.MarkReadInNotificationFeed();

        read.DismissFromNotificationFeed();
        read.DismissFromNotificationFeed();

        read.DismissedFromNotificationFeed.ShouldBeTrue();
        read.ReadInNotificationFeed.ShouldBeTrue();
    }

    [Fact]
    public void DismissFromNotificationFeed_WorksAfterTheMailIsClaimedOrSent()
    {
        var queued = CreateValid();
        queued.MarkQueued();
        queued.DismissFromNotificationFeed();
        queued.DismissedFromNotificationFeed.ShouldBeTrue();

        var sent = CreateValid();
        sent.MarkQueued();
        sent.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));
        sent.DismissFromNotificationFeed();
        sent.DismissedFromNotificationFeed.ShouldBeTrue();
        sent.NotificationStatus.ShouldBe(FollowedCompanyAdHitStatus.Sent);
    }

    [Fact]
    public void NeitherMarkSeenNorTheMailStateMachineNorAReadMark_ChangesTheDismissedFlag()
    {
        var undismissed = CreateValid();
        undismissed.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));
        undismissed.MarkReadInNotificationFeed();
        undismissed.MarkQueued();
        undismissed.MarkSent(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(5)));
        undismissed.DismissedFromNotificationFeed.ShouldBeFalse("opening, reading and mailing a hit never dismiss it");

        var dismissed = CreateValid();
        dismissed.DismissFromNotificationFeed();
        dismissed.MarkSeen(FakeDateTimeProvider.At(Clock.UtcNow.AddMinutes(10)));
        dismissed.MarkReadInNotificationFeed();
        dismissed.MarkQueued();
        dismissed.MarkFailed();
        dismissed.DismissedFromNotificationFeed.ShouldBeTrue("nothing clears a dismissal");
        dismissed.ReadInNotificationFeed.ShouldBeTrue();
    }
}
