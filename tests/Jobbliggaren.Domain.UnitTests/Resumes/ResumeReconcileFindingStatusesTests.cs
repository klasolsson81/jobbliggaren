using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

// Fas 4b CV-motor v2 PR-8.1 (issue #657, epic #649, ADR 0093, local ADR 0097 §5; CTO-bind
// docs/reviews/2026-07-09-fas4b-cv-v2-pr8-cto.md Q1/Q5/Q6). The version-scoped RECONCILE path on
// the Resume aggregate: Resume.ReconcileFindingStatuses seeds/refreshes Open rows for the review's
// currently-actionable (Fail/Warn) findings, drops now-passing Open rows, and NEVER touches a
// user's Resolved/Ignored decision. It is the batch complement to SetFindingStatus (the single-row
// upsert) and, unlike it, stamps ReviewedRubricVersion + raises ResumeReviewReconciledDomainEvent.
// Version-scoped: rows of a different rubric version are inert (ADR 0097 §5 non-carry).
//
// SPEC-DRIVEN pins (Result/DomainError idiom — assert result.IsFailure + result.Error.Code, never a
// reading of the implementation). RED until Resume.ReconcileFindingStatuses + ReviewFindingSnapshot
// + ResumeReviewReconciledDomainEvent + Resume.ReviewedRubricVersion ship.
public class ResumeReconcileFindingStatusesTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());

    private const string Version = "2.1.0";

    // Distinct, shape-valid fingerprints (64 lowercase hex; the aggregate validates shape, never
    // provenance — the reconciler derives the real digest, covered in ResumeReviewReconcilerTests).
    private static readonly string FpA = new('a', 64);
    private static readonly string FpB = new('b', 64);
    private static readonly string FpC = new('c', 64);

    private static Resume CreateResume() =>
        Resume.Create(ValidJobSeekerId, "Mitt CV", "Klas Olsson", Clock).Value;

    private static FakeDateTimeProvider LaterClock(int hours) =>
        FakeDateTimeProvider.At(Clock.UtcNow.AddHours(hours));

    private static ReviewFindingSnapshot[] Actionable(params ReviewFindingSnapshot[] items) =>
        items;

    // ---------------------------------------------------------------
    // Seed — a new actionable finding with no existing row
    // ---------------------------------------------------------------

    [Fact]
    public void ReconcileFindingStatuses_WithNewActionableFinding_SeedsOpenRow_StampsVersion_RaisesEvent()
    {
        var resume = CreateResume();
        resume.ClearDomainEvents();
        var clock = LaterClock(1);

        var result = resume.ReconcileFindingStatuses(
            Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), clock);

        result.IsSuccess.ShouldBeTrue();
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A1");
        row.RubricVersion.ShouldBe(Version);
        row.Status.ShouldBe(ReviewFindingStatus.Open);
        row.TargetFingerprint.ShouldBe(FpA);
        row.StaleAt.ShouldBeNull();
        // Reviewed-and-stamped, and the parent stamp advances (load-bearing for xmin concurrency).
        resume.ReviewedRubricVersion.ShouldBe(Version);
        resume.UpdatedAt.ShouldBe(clock.UtcNow);

        var evt = resume.DomainEvents.OfType<ResumeReviewReconciledDomainEvent>().ShouldHaveSingleItem();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.RubricVersion.ShouldBe(Version);
        evt.OpenCount.ShouldBe(1);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void ReconcileFindingStatuses_WithSameActionableTwice_IsIdempotent_KeepsSingleOpenRow()
    {
        var resume = CreateResume();

        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), Clock);
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), LaterClock(1));

        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A1");
        row.Status.ShouldBe(ReviewFindingStatus.Open);
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenSnapshotFingerprintDiffers_RefreshesFingerprintOnOpenRow()
    {
        var resume = CreateResume();
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), Clock);

        // The finding is still present but its evidence moved → a new fingerprint on the SAME row.
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpB)), LaterClock(1));

        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.Status.ShouldBe(ReviewFindingStatus.Open);
        row.TargetFingerprint.ShouldBe(FpB);
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenOpenCriterionNoLongerActionable_DropsTheOpenRow()
    {
        var resume = CreateResume();
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), Clock);
        resume.FindingStatuses.ShouldHaveSingleItem();

        // A1 is no longer in the actionable set (the finding passed) → its Open row is removed.
        resume.ReconcileFindingStatuses(Version, Actionable(), LaterClock(1));

        resume.FindingStatuses.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // NEVER touches a user's decision (Resolved / Ignored)
    // ---------------------------------------------------------------

    [Fact]
    public void ReconcileFindingStatuses_WhenCriterionAbsentFromActionable_NeverTouchesResolvedRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus(Version, "A2", ReviewFindingStatus.Resolved, FpA, Clock);

        // A2 is not in the actionable set; a Resolved decision is content-scoped and must survive
        // untouched (only UpdateMasterContent may stamp it stale — never reconcile).
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpB)), LaterClock(1));

        var a2 = resume.FindingStatuses.Single(f => f.CriterionId == "A2");
        a2.Status.ShouldBe(ReviewFindingStatus.Resolved);
        a2.TargetFingerprint.ShouldBe(FpA);
        a2.StaleAt.ShouldBeNull();
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenCriterionAbsentFromActionable_NeverTouchesIgnoredRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus(Version, "C2", ReviewFindingStatus.Ignored, FpA, Clock);

        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpB)), LaterClock(1));

        var c2 = resume.FindingStatuses.Single(f => f.CriterionId == "C2");
        c2.Status.ShouldBe(ReviewFindingStatus.Ignored);
        c2.TargetFingerprint.ShouldBe(FpA);
        c2.StaleAt.ShouldBeNull();
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenActionableCriterionAlreadyResolved_KeepsResolved_NoSecondRow_NoFlip()
    {
        var resume = CreateResume();
        resume.SetFindingStatus(Version, "A2", ReviewFindingStatus.Resolved, FpA, Clock);

        // A2 is actionable again in the fresh review, but the user already Resolved it — reconcile
        // must not add a second Open row and must not flip the decision back to Open.
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A2", FpB)), LaterClock(1));

        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A2");
        row.Status.ShouldBe(ReviewFindingStatus.Resolved);
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenActionableCriterionAlreadyIgnored_KeepsIgnored_NoSecondRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus(Version, "C2", ReviewFindingStatus.Ignored, FpA, Clock);

        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("C2", FpB)), LaterClock(1));

        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("C2");
        row.Status.ShouldBe(ReviewFindingStatus.Ignored);
    }

    // ---------------------------------------------------------------
    // Version-scoped (ADR 0097 §5 non-carry): a different rubric version is inert
    // ---------------------------------------------------------------

    [Fact]
    public void ReconcileFindingStatuses_IsVersionScoped_NeverSeedsDropsOrModifiesRowsOfAnotherVersion()
    {
        var resume = CreateResume();
        // Rows recorded against an OLDER rubric version.
        resume.SetFindingStatus("1.2.0", "A1", ReviewFindingStatus.Open, FpA, Clock);
        resume.SetFindingStatus("1.2.0", "A2", ReviewFindingStatus.Resolved, FpB, Clock);

        // Reconciling at 2.1.0 with an empty actionable set must not drop the 1.2.0 Open row, nor
        // touch the 1.2.0 Resolved row — a new rubric version is a new key space.
        resume.ReconcileFindingStatuses(Version, Actionable(), LaterClock(1));

        var oldOpen = resume.FindingStatuses.Single(f => f.RubricVersion == "1.2.0" && f.CriterionId == "A1");
        var oldResolved = resume.FindingStatuses.Single(f => f.RubricVersion == "1.2.0" && f.CriterionId == "A2");
        oldOpen.Status.ShouldBe(ReviewFindingStatus.Open);
        oldResolved.Status.ShouldBe(ReviewFindingStatus.Resolved);
        oldResolved.TargetFingerprint.ShouldBe(FpB);
    }

    [Fact]
    public void ReconcileFindingStatuses_WhenActionableEmpty_StampsReviewedVersion_AndDropsNowPassingOpenRows()
    {
        var resume = CreateResume();
        resume.ReconcileFindingStatuses(Version, Actionable(new ReviewFindingSnapshot("A1", FpA)), Clock);
        resume.ClearDomainEvents();
        var clock = LaterClock(1);

        // Reviewed-and-clean is a real state: the CV was assessed, everything passed. The stamp still
        // lands, the now-passing Open row is dropped, and the event reports zero open findings.
        var result = resume.ReconcileFindingStatuses(Version, Actionable(), clock);

        result.IsSuccess.ShouldBeTrue();
        resume.FindingStatuses.ShouldBeEmpty();
        resume.ReviewedRubricVersion.ShouldBe(Version);
        var evt = resume.DomainEvents.OfType<ResumeReviewReconciledDomainEvent>().ShouldHaveSingleItem();
        evt.OpenCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // Validation failures — Result.Failure, nothing mutated, no stamp
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]         // null
    [InlineData("x.y")]        // non-numeric / two parts
    [InlineData("1.2")]        // two parts
    [InlineData("12345.0.0")]  // a part longer than four digits
    public void ReconcileFindingStatuses_WithInvalidRubricVersion_ReturnsValidation_NothingMutated(string? version)
    {
        var resume = CreateResume();

        var result = resume.ReconcileFindingStatuses(
            version, Actionable(new ReviewFindingSnapshot("A1", FpA)), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.RubricVersionInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
        resume.ReviewedRubricVersion.ShouldBeNull();
    }

    [Fact]
    public void ReconcileFindingStatuses_WithNullActionableList_ReturnsValidation_NothingMutated()
    {
        var resume = CreateResume();

        var result = resume.ReconcileFindingStatuses(Version, null, Clock);

        result.IsFailure.ShouldBeTrue();
        // A missing actionable set is not the same as an empty one (which is "reviewed, all passed").
        result.Error.Code.ShouldBe("Resume.ActionableFindingsRequired");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
        resume.ReviewedRubricVersion.ShouldBeNull();
    }

    [Fact]
    public void ReconcileFindingStatuses_WithInvalidSnapshotCriterionId_ReturnsValidation_NothingMutated()
    {
        var resume = CreateResume();

        // "Q99x" is length 4 — the criterion-id shape is one upper letter + 1–2 digits.
        var result = resume.ReconcileFindingStatuses(
            Version, Actionable(new ReviewFindingSnapshot("Q99x", FpA)), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.CriterionIdInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
        resume.ReviewedRubricVersion.ShouldBeNull();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]   // 63 chars
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]  // uppercase hex
    public void ReconcileFindingStatuses_WithInvalidSnapshotFingerprint_ReturnsValidation_NothingMutated(
        string fingerprint)
    {
        var resume = CreateResume();

        var result = resume.ReconcileFindingStatuses(
            Version, Actionable(new ReviewFindingSnapshot("A1", fingerprint)), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FingerprintInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
        resume.ReviewedRubricVersion.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Duplicate criterion ids in one actionable list — pinned semantics: dedupe, first wins
    // ---------------------------------------------------------------

    [Fact]
    public void ReconcileFindingStatuses_WithDuplicateCriterionIds_DedupesFirstWins_SingleOpenRow()
    {
        var resume = CreateResume();

        // Same criterion twice in one batch (a defensive input); the pinned contract is dedupe with
        // first-write-wins, so exactly one Open row survives carrying the FIRST snapshot fingerprint.
        var result = resume.ReconcileFindingStatuses(
            Version,
            Actionable(new ReviewFindingSnapshot("A1", FpA), new ReviewFindingSnapshot("A1", FpC)),
            Clock);

        result.IsSuccess.ShouldBeTrue();
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.CriterionId.ShouldBe("A1");
        row.Status.ShouldBe(ReviewFindingStatus.Open);
        row.TargetFingerprint.ShouldBe(FpA);
    }
}
