using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// RÖD svit (TDD — implementation följer). Spec: issue #315 / ADR 0086 —
// Application.CreateFromJobAd, den ENDA writer som sätter ett AdSnapshot
// (snapshot ⇒ JobAdId; ManualPosting alltid null). Symmetri med
// ManualPosting-XOR. Capture sker uppströms i handlern; aggregatet tar emot
// ett färdigbyggt, validation-fritt VO.
public class ApplicationCreateFromJobAdTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static AdSnapshot ValidSnapshot() =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: "1gEC_kvM_TXK",
            url: "https://example.com/jobb/1",
            source: "Platsbanken",
            publishedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            description: "En beskrivning.",
            capturedAt: Clock.UtcNow);

    // ---------------------------------------------------------------
    // Success — Draft, JobAdId + AdSnapshot satta, ManualPosting null
    // ---------------------------------------------------------------

    [Fact]
    public void CreateFromJobAd_WithValidData_ReturnsSuccessAsDraft()
    {
        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ApplicationStatus.Draft);
        result.Value.JobSeekerId.ShouldBe(ValidJobSeekerId);
        result.Value.JobAdId.ShouldBe(ValidJobAdId);
    }

    [Fact]
    public void CreateFromJobAd_WithValidData_SetsAdSnapshotToThePassedVo()
    {
        var snapshot = ValidSnapshot();

        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, snapshot, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AdSnapshot.ShouldNotBeNull();
        result.Value.AdSnapshot.ShouldBe(snapshot);
    }

    [Fact]
    public void CreateFromJobAd_WithValidData_LeavesManualPostingNull()
    {
        // En JobAd-kopplad ansökan är aldrig manuell (XOR-symmetri).
        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public void CreateFromJobAd_WithValidData_RaisesApplicationCreatedDomainEvent()
    {
        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), null, Clock);

        result.IsSuccess.ShouldBeTrue();
        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationCreatedDomainEvent>();
        evt.JobSeekerId.ShouldBe(ValidJobSeekerId);
        evt.JobAdId.ShouldBe(ValidJobAdId);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void CreateFromJobAd_TrimsCoverLetter()
    {
        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), "  Hej  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CoverLetter.ShouldBe("Hej");
    }

    // ---------------------------------------------------------------
    // Validering — Result-fel (förväntade fel, ej exceptions)
    // ---------------------------------------------------------------

    [Fact]
    public void CreateFromJobAd_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = Application.CreateFromJobAd(
            default, ValidJobAdId, ValidSnapshot(), null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobSeekerIdRequired");
    }

    [Fact]
    public void CreateFromJobAd_WithDefaultJobAdId_ReturnsFailure()
    {
        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, default, ValidSnapshot(), null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobAdIdRequired");
    }

    [Fact]
    public void CreateFromJobAd_WithCoverLetterAtMaxLength_ReturnsSuccess()
    {
        var coverLetter = new string('A', 10_000);

        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), coverLetter, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CreateFromJobAd_WithCoverLetterExceedingMaxLength_ReturnsFailure()
    {
        var tooLong = new string('A', 10_001);

        var result = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, ValidSnapshot(), tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.CoverLetterTooLong");
    }

    // ---------------------------------------------------------------
    // Oväntat fel — null adSnapshot kastar (ArgumentNullException.ThrowIfNull),
    // det är inte ett förväntat valideringsfel (exception-idiomet, CLAUDE.md §3).
    // ---------------------------------------------------------------

    [Fact]
    public void CreateFromJobAd_WithNullAdSnapshot_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            Application.CreateFromJobAd(
                ValidJobSeekerId, ValidJobAdId, adSnapshot: null!, null, Clock));
    }
}
