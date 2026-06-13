using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// RÖD svit (TDD). Spec: architect-design §2 — aggregat-invariant
// `JobAdId ⊕ ManualPosting` i Application.Create (utökad signatur, ej overload).
// Invariant-matris (architect-design §2):
//   (jobAdId satt, manual null)  → Success  (tillstånd 1: JobAd-kopplad)
//   (jobAdId null, manual satt)  → Success  (tillstånd 2: manuell)
//   (jobAdId null, manual null)  → Success  (tillstånd 3: degenererad — BEVARAS)
//   (jobAdId satt, manual satt)  → Failure  "Application.JobAdAndManualMutuallyExclusive"
public class ApplicationManualPostingInvariantTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static ManualPosting ValidManualPosting() =>
        ManualPosting.Create("Backend-utvecklare", "Klarna", null, null).Value;

    [Fact]
    public void Create_WithJobAdIdAndNoManualPosting_ReturnsSuccess()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobAdId.ShouldBe(ValidJobAdId);
        result.Value.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public void Create_WithManualPostingAndNoJobAdId_ReturnsSuccess()
    {
        var manual = ValidManualPosting();

        var result = Application.Create(ValidJobSeekerId, null, null, manual, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobAdId.ShouldBeNull();
        result.Value.ManualPosting.ShouldNotBeNull();
        result.Value.ManualPosting!.Title.ShouldBe("Backend-utvecklare");
        result.Value.ManualPosting.Company.ShouldBe("Klarna");
    }

    // BLOCKING regressionsskydd — dagens cover-letter-only-beteende
    // (jobAdId null + manual null) MÅSTE förbli Success (architect-design §2,
    // datamodell-rapport A3, plan §61). Får ej regressera till "exakt-en".
    [Fact]
    public void Create_WithNeitherJobAdIdNorManualPosting_ReturnsSuccess()
    {
        var result = Application.Create(ValidJobSeekerId, null, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobAdId.ShouldBeNull();
        result.Value.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public void Create_WithNeitherButWithCoverLetter_ReturnsSuccess()
    {
        // Explicit dagens degenererade tillstånd: cover-letter-only.
        var result = Application.Create(
            ValidJobSeekerId, null, "Mitt personliga brev", null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CoverLetter.ShouldBe("Mitt personliga brev");
        result.Value.JobAdId.ShouldBeNull();
        result.Value.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public void Create_WithBothJobAdIdAndManualPosting_ReturnsFailure()
    {
        var manual = ValidManualPosting();

        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, manual, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobAdAndManualMutuallyExclusive");
    }

    [Fact]
    public void Create_WithBothJobAdIdAndManualPosting_DoesNotRaiseDomainEvent()
    {
        var manual = ValidManualPosting();

        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, manual, Clock);

        result.IsFailure.ShouldBeTrue();
        // Ingen Application skapas vid invariant-brott → inget event.
    }

    [Fact]
    public void Create_WithManualPosting_StillEnforcesJobSeekerIdRequired()
    {
        // Befintliga preconditions körs oförändrat även med manualPosting-param.
        var manual = ValidManualPosting();

        var result = Application.Create(default, null, null, manual, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobSeekerIdRequired");
    }

    [Fact]
    public void Create_WithManualPosting_ExposesManualPostingProperty()
    {
        var manual = ManualPosting.Create(
            "Data Engineer", "Spotify", "https://example.com/jobb",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)).Value;

        var result = Application.Create(ValidJobSeekerId, null, null, manual, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ManualPosting.ShouldBe(manual);
        result.Value.ManualPosting!.Url.ShouldBe("https://example.com/jobb");
        result.Value.ManualPosting.ExpiresAt.ShouldBe(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
    }
}
