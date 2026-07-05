using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

// Fas 4b CV-motor v2 PR-4 (issue #653, epic #649, ADR 0093 §D2(e), local ADR 0097). The
// DEK-free finding-status ledger on the Resume aggregate: Resume.SetFindingStatus is the SINGLE
// mutation path (upsert keyed on (rubricVersion, criterionId)), a content change marks previously
// Resolved rows stale (orthogonal to the user's decision), and ReviewFindingStatus is the closed,
// Name-persisted decision vocabulary.
//
// SPEC-STYLE pins: the assertions describe the intended contract (Result/DomainError idiom —
// assert result.IsFailure + result.Error.Code), not a reading of the implementation.
public class ResumeFindingStatusTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());

    // Two distinct, shape-valid fingerprints (64 lowercase hex; the aggregate validates shape,
    // never provenance — the handler derives the real digest, PR-4 unit tests cover that).
    private const string FingerprintA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string FingerprintB = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private static Resume CreateResume() =>
        Resume.Create(ValidJobSeekerId, "Mitt CV", "Klas Olsson", Clock).Value;

    private static ResumeContent ValidContent() => ResumeContent.Empty("Klas Olsson");

    private static FakeDateTimeProvider LaterClock(int hours) =>
        FakeDateTimeProvider.At(Clock.UtcNow.AddHours(hours));

    // ---------------------------------------------------------------
    // SetFindingStatus — happy path (insert)
    // ---------------------------------------------------------------

    [Fact]
    public void SetFindingStatus_OnFirstDecision_InsertsRowAndRaisesEvent()
    {
        var resume = CreateResume();
        resume.ClearDomainEvents();
        var clock = LaterClock(1);

        var result = resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, clock);

        result.IsSuccess.ShouldBeTrue();
        var row = resume.FindingStatuses.ShouldHaveSingleItem();
        row.RubricVersion.ShouldBe("1.1.0");
        row.CriterionId.ShouldBe("A7");
        row.Status.ShouldBe(ReviewFindingStatus.Resolved);
        row.TargetFingerprint.ShouldBe(FingerprintA);
        row.StaleAt.ShouldBeNull();
        row.CreatedAt.ShouldBe(clock.UtcNow);
        row.UpdatedAt.ShouldBe(clock.UtcNow);
        resume.UpdatedAt.ShouldBe(clock.UtcNow);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeFindingStatusChangedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.RubricVersion.ShouldBe("1.1.0");
        evt.CriterionId.ShouldBe("A7");
        evt.Status.ShouldBe(ReviewFindingStatus.Resolved);
        evt.OccurredAt.ShouldBe(clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // SetFindingStatus — upsert (same key updates the SAME row)
    // ---------------------------------------------------------------

    [Fact]
    public void SetFindingStatus_OnSameKey_UpdatesSameRow_ReplacesStatusAndFingerprint_ClearsStaleness()
    {
        var resume = CreateResume();
        // First decision → Resolved, then a content change stamps staleness on it.
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        resume.UpdateMasterContent(ValidContent(), LaterClock(1));
        resume.FindingStatuses.ShouldHaveSingleItem().StaleAt.ShouldNotBeNull();

        var clock = LaterClock(2);
        var result = resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Ignored, FingerprintB, clock);

        result.IsSuccess.ShouldBeTrue();
        // Upsert: the (rubricVersion, criterionId) key stays a single row, never a duplicate.
        resume.FindingStatuses.Count.ShouldBe(1);
        var row = resume.FindingStatuses[0];
        row.Status.ShouldBe(ReviewFindingStatus.Ignored);
        row.TargetFingerprint.ShouldBe(FingerprintB);
        // A fresh decision clears the staleness stamp.
        row.StaleAt.ShouldBeNull();
        // CreatedAt is the original insert; UpdatedAt advances.
        row.CreatedAt.ShouldBe(Clock.UtcNow);
        row.UpdatedAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void SetFindingStatus_ForDifferentRubricVersionsOrCriteria_CreatesSeparateRows()
    {
        var resume = CreateResume();

        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        resume.SetFindingStatus("1.2.0", "A7", ReviewFindingStatus.Resolved, FingerprintB, Clock); // other version
        resume.SetFindingStatus("1.1.0", "A8", ReviewFindingStatus.Ignored, FingerprintB, Clock);  // other criterion

        resume.FindingStatuses.Count.ShouldBe(3);
    }

    // ---------------------------------------------------------------
    // SetFindingStatus — validation failures (Result.Failure + code)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("1.1")]        // two parts
    [InlineData("x.y.z")]      // non-numeric
    [InlineData("")]           // empty
    [InlineData(null)]         // null
    [InlineData("12345.0.0")]  // a part longer than four digits
    [InlineData("1.0.0.0")]    // four parts
    public void SetFindingStatus_WithInvalidRubricVersion_ReturnsValidation(string? rubricVersion)
    {
        var resume = CreateResume();

        var result = resume.SetFindingStatus(
            rubricVersion, "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.RubricVersionInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a1")]    // lowercase category letter
    [InlineData("A123")]  // three digits (length 4)
    [InlineData("1A")]    // digit first
    [InlineData("")]      // empty
    [InlineData(null)]    // null
    public void SetFindingStatus_WithInvalidCriterionId_ReturnsValidation(string? criterionId)
    {
        var resume = CreateResume();

        var result = resume.SetFindingStatus(
            "1.1.0", criterionId, ReviewFindingStatus.Resolved, FingerprintA, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.CriterionIdInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
    }

    [Fact]
    public void SetFindingStatus_WithNullStatus_ReturnsValidation()
    {
        var resume = CreateResume();

        var result = resume.SetFindingStatus("1.1.0", "A7", null, FingerprintA, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingStatusRequired");
        resume.FindingStatuses.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]   // 63 chars
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")] // 65 chars
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]  // uppercase hex
    [InlineData("0123456789abcdeg0123456789abcdef0123456789abcdef0123456789abcdef")]  // non-hex 'g'
    [InlineData(null)]                                                                // null
    public void SetFindingStatus_WithInvalidFingerprint_ReturnsValidation(string? fingerprint)
    {
        var resume = CreateResume();

        var result = resume.SetFindingStatus(
            "1.1.0", "A7", ReviewFindingStatus.Resolved, fingerprint, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FingerprintInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.FindingStatuses.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // UpdateMasterContent — staleness stamp (CTO-bind PR-4 Q3)
    // ---------------------------------------------------------------

    [Fact]
    public void UpdateMasterContent_StampsStaleAt_OnResolvedRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        resume.FindingStatuses[0].StaleAt.ShouldBeNull();
        var clock = LaterClock(1);

        resume.UpdateMasterContent(ValidContent(), clock);

        resume.FindingStatuses[0].StaleAt.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void UpdateMasterContent_DoesNotStampStaleAt_OnIgnoredRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Ignored, FingerprintA, Clock);

        resume.UpdateMasterContent(ValidContent(), LaterClock(1));

        // Ignored is a content-independent rule opt-out — it never goes stale.
        resume.FindingStatuses[0].StaleAt.ShouldBeNull();
    }

    [Fact]
    public void UpdateMasterContent_DoesNotStampStaleAt_OnOpenRow()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Open, FingerprintA, Clock);

        resume.UpdateMasterContent(ValidContent(), LaterClock(1));

        // Open has no recorded decision to invalidate.
        resume.FindingStatuses[0].StaleAt.ShouldBeNull();
    }

    [Fact]
    public void UpdateMasterContent_IsIdempotentForStaleness_KeepsFirstStaleAt()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        var firstStaleClock = LaterClock(1);
        var secondStaleClock = LaterClock(5);

        resume.UpdateMasterContent(ValidContent(), firstStaleClock);
        resume.UpdateMasterContent(ValidContent(), secondStaleClock);

        // An already-stale row keeps its FIRST stamp — the second content change does not re-stamp.
        resume.FindingStatuses[0].StaleAt.ShouldBe(firstStaleClock.UtcNow);
    }

    [Fact]
    public void UpdateMasterContent_StampsStaleness_InTheSameCallThatRaisesContentUpdatedEvent()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        resume.ClearDomainEvents();
        var clock = LaterClock(1);

        resume.UpdateMasterContent(ValidContent(), clock);

        var evt = resume.DomainEvents.OfType<ResumeContentUpdatedDomainEvent>().ShouldHaveSingleItem();
        // Same call, same clock: the stale stamp is transactional with the content write.
        resume.FindingStatuses[0].StaleAt.ShouldBe(clock.UtcNow);
        resume.FindingStatuses[0].StaleAt.ShouldBe(evt.OccurredAt);
    }

    [Fact]
    public void SetFindingStatus_AfterStaleness_ClearsStaleAt()
    {
        var resume = CreateResume();
        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintA, Clock);
        resume.UpdateMasterContent(ValidContent(), LaterClock(1));
        resume.FindingStatuses[0].StaleAt.ShouldNotBeNull();

        resume.SetFindingStatus("1.1.0", "A7", ReviewFindingStatus.Resolved, FingerprintB, LaterClock(2));

        resume.FindingStatuses[0].StaleAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // ReviewFindingStatus — persisted-name + value contract
    // ---------------------------------------------------------------

    [Fact]
    public void ReviewFindingStatus_PersistedNamesAndValues_AreStable()
    {
        // Name is the stored column vocabulary (ResumeSourceOrigin parity); a rename must fail here.
        ReviewFindingStatus.Open.Name.ShouldBe("Open");
        ReviewFindingStatus.Resolved.Name.ShouldBe("Resolved");
        ReviewFindingStatus.Ignored.Name.ShouldBe("Ignored");

        ReviewFindingStatus.Open.Value.ShouldBe(0);
        ReviewFindingStatus.Resolved.Value.ShouldBe(1);
        ReviewFindingStatus.Ignored.Value.ShouldBe(2);
    }
}
