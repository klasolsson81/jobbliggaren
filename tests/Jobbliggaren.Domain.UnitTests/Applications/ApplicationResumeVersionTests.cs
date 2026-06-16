using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// RÖD svit (TDD) — F4-11: en submittad Application refererar exakt den
// ResumeVersionId (CV-version) som användes vid ansökan. Bakåtkompatibel
// (befintliga ansökningar → null). Deterministisk, ingen AI.
//
// Spec (CTO-bunden): Application.ResumeVersionId (nullable),
// CanAttachResumeVersion() (true utom Accepted/Rejected/Withdrawn),
// AttachResumeVersion(versionId, clock) → Result, höjer
// ApplicationResumeVersionAttachedDomainEvent. Replace tillåts medan
// non-terminal (last wins). default(ResumeVersionId) avvisas.
public class ApplicationResumeVersionTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    // ---------------------------------------------------------------
    // Create — initialtillstånd
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_LeavesResumeVersionIdNull()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResumeVersionId.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // AttachResumeVersion — happy path (non-terminala statusar)
    // ---------------------------------------------------------------

    [Fact]
    public void AttachResumeVersion_WhenDraft_ReturnsSuccessAndSetsId()
    {
        var application = CreateValidApplication();
        var versionId = ResumeVersionId.New();

        var result = application.AttachResumeVersion(versionId, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.ResumeVersionId.ShouldBe(versionId);
    }

    [Fact]
    public void AttachResumeVersion_WhenSubmitted_ReturnsSuccessAndSetsId()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Submitted);
        var versionId = ResumeVersionId.New();

        var result = application.AttachResumeVersion(versionId, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.ResumeVersionId.ShouldBe(versionId);
    }

    [Fact]
    public void AttachResumeVersion_WhenGhosted_ReturnsSuccess()
    {
        // Ghosted är reaktiverbar → bifoga ska vara tillåtet.
        var application = CreateApplicationAtStatus(ApplicationStatus.Ghosted);
        var versionId = ResumeVersionId.New();

        var result = application.AttachResumeVersion(versionId, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.ResumeVersionId.ShouldBe(versionId);
    }

    [Fact]
    public void AttachResumeVersion_WhenValid_UpdatesUpdatedAt()
    {
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        var application = CreateValidApplication();

        application.AttachResumeVersion(ResumeVersionId.New(), laterClock);

        application.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // AttachResumeVersion — domain event
    // ---------------------------------------------------------------

    [Fact]
    public void AttachResumeVersion_WhenValid_RaisesApplicationResumeVersionAttachedDomainEvent()
    {
        var application = CreateValidApplication();
        application.ClearDomainEvents();
        var versionId = ResumeVersionId.New();

        application.AttachResumeVersion(versionId, Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationResumeVersionAttachedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.JobSeekerId.ShouldBe(application.JobSeekerId);
        evt.ResumeVersionId.ShouldBe(versionId);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // AttachResumeVersion — replace tillåtet medan non-terminal (last wins)
    // ---------------------------------------------------------------

    [Fact]
    public void AttachResumeVersion_TwiceWithDifferentIds_LastWins()
    {
        var application = CreateValidApplication();
        var firstVersion = ResumeVersionId.New();
        var secondVersion = ResumeVersionId.New();

        application.AttachResumeVersion(firstVersion, Clock);
        var result = application.AttachResumeVersion(secondVersion, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.ResumeVersionId.ShouldBe(secondVersion);
    }

    [Fact]
    public void AttachResumeVersion_TwiceWithDifferentIds_RaisesEventEachTime()
    {
        var application = CreateValidApplication();
        application.ClearDomainEvents();

        application.AttachResumeVersion(ResumeVersionId.New(), Clock);
        application.AttachResumeVersion(ResumeVersionId.New(), Clock);

        application.DomainEvents
            .OfType<ApplicationResumeVersionAttachedDomainEvent>()
            .Count()
            .ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // AttachResumeVersion — default(ResumeVersionId) avvisas
    // ---------------------------------------------------------------

    [Fact]
    public void AttachResumeVersion_WithDefaultVersionId_ReturnsFailure()
    {
        var application = CreateValidApplication();

        var result = application.AttachResumeVersion(default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.ResumeVersionIdRequired");
    }

    [Fact]
    public void AttachResumeVersion_WithDefaultVersionId_DoesNotMutateOrRaiseEvent()
    {
        var application = CreateValidApplication();
        application.ClearDomainEvents();

        application.AttachResumeVersion(default, Clock);

        application.ResumeVersionId.ShouldBeNull();
        application.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // AttachResumeVersion — terminala statusar blockeras
    // ---------------------------------------------------------------

    [Fact]
    public void AttachResumeVersion_WhenAccepted_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);

        var result = application.AttachResumeVersion(ResumeVersionId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.ResumeVersionAttachNotAllowed");
    }

    [Fact]
    public void AttachResumeVersion_WhenRejected_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Rejected);

        var result = application.AttachResumeVersion(ResumeVersionId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.ResumeVersionAttachNotAllowed");
    }

    [Fact]
    public void AttachResumeVersion_WhenWithdrawn_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Withdrawn);

        var result = application.AttachResumeVersion(ResumeVersionId.New(), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.ResumeVersionAttachNotAllowed");
    }

    [Fact]
    public void AttachResumeVersion_WhenAccepted_DoesNotMutateOrRaiseEvent()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);
        application.ClearDomainEvents();

        application.AttachResumeVersion(ResumeVersionId.New(), Clock);

        application.ResumeVersionId.ShouldBeNull();
        application.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // CanAttachResumeVersion — sanningstabell över alla 10 statusar
    // (true utom Accepted/Rejected/Withdrawn)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Submitted", true)]
    [InlineData("Acknowledged", true)]
    [InlineData("InterviewScheduled", true)]
    [InlineData("Interviewing", true)]
    [InlineData("OfferReceived", true)]
    [InlineData("Ghosted", true)]
    [InlineData("Accepted", false)]
    [InlineData("Rejected", false)]
    [InlineData("Withdrawn", false)]
    public void CanAttachResumeVersion_AcrossAllStatuses_MatchesTruthTable(
        string statusName, bool expected)
    {
        var status = ApplicationStatus.FromName(statusName);
        var application = CreateApplicationAtStatus(status);

        application.CanAttachResumeVersion().ShouldBe(expected);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateValidApplication() =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock).Value;

    /// <summary>
    /// Bygger en Application vars Status är satt till <paramref name="target"/>.
    /// Använder transition-/MarkGhosted-vägar för att nå varje status utan att
    /// exponera privat state (samma mönster som ApplicationTests, utökat med
    /// Ghosted-vägen).
    /// </summary>
    private static Application CreateApplicationAtStatus(ApplicationStatus target)
    {
        var app = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock).Value;

        if (target == ApplicationStatus.Draft) return app;

        app.TransitionTo(ApplicationStatus.Submitted, Clock);
        if (target == ApplicationStatus.Submitted) return app;

        // Ghosted nås via MarkGhosted (automatisk), inte TransitionTo.
        if (target == ApplicationStatus.Ghosted)
        {
            app.MarkGhosted(Clock);
            return app;
        }

        if (target == ApplicationStatus.Rejected)
        {
            app.TransitionTo(ApplicationStatus.Rejected, Clock);
            return app;
        }
        if (target == ApplicationStatus.Withdrawn)
        {
            app.TransitionTo(ApplicationStatus.Withdrawn, Clock);
            return app;
        }

        app.TransitionTo(ApplicationStatus.Acknowledged, Clock);
        if (target == ApplicationStatus.Acknowledged) return app;

        app.TransitionTo(ApplicationStatus.InterviewScheduled, Clock);
        if (target == ApplicationStatus.InterviewScheduled) return app;

        app.TransitionTo(ApplicationStatus.Interviewing, Clock);
        if (target == ApplicationStatus.Interviewing) return app;

        app.TransitionTo(ApplicationStatus.OfferReceived, Clock);
        if (target == ApplicationStatus.OfferReceived) return app;

        if (target == ApplicationStatus.Accepted)
        {
            app.TransitionTo(ApplicationStatus.Accepted, Clock);
            return app;
        }

        throw new InvalidOperationException($"Okänt målstatus: {target}");
    }
}
