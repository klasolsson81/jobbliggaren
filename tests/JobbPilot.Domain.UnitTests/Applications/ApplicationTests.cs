using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Applications.Events;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.UnitTests.JobAds;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Applications;

public class ApplicationTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    // ---------------------------------------------------------------
    // Create — validering
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_ReturnsSuccess()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobSeekerId.ShouldBe(ValidJobSeekerId);
        result.Value.JobAdId.ShouldBe(ValidJobAdId);
        result.Value.Status.ShouldBe(ApplicationStatus.Draft);
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.UpdatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Create_WithoutJobAdId_ReturnsSuccess()
    {
        var result = Application.Create(ValidJobSeekerId, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobAdId.ShouldBeNull();
    }

    [Fact]
    public void Create_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = Application.Create(default, ValidJobAdId, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobSeekerIdRequired");
    }

    [Fact]
    public void Create_WithCoverLetterAtMaxLength_ReturnsSuccess()
    {
        var coverLetter = new string('A', 10_000);

        var result = Application.Create(ValidJobSeekerId, null, coverLetter, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithCoverLetterExceedingMaxLength_ReturnsFailure()
    {
        var tooLong = new string('A', 10_001);

        var result = Application.Create(ValidJobSeekerId, null, tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.CoverLetterTooLong");
    }

    [Fact]
    public void Create_TrimsCoverLetter()
    {
        var result = Application.Create(ValidJobSeekerId, null, "  Hej  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CoverLetter.ShouldBe("Hej");
    }

    // ---------------------------------------------------------------
    // Create — domain event
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_RaisesApplicationCreatedDomainEvent()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationCreatedDomainEvent>();
        evt.JobSeekerId.ShouldBe(ValidJobSeekerId);
        evt.JobAdId.ShouldBe(ValidJobAdId);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Create_WithDefaultJobSeekerId_DoesNotRaiseDomainEvent()
    {
        var result = Application.Create(default, ValidJobAdId, null, Clock);

        result.IsFailure.ShouldBeTrue();
        // Ingen Application skapas — inget event
    }

    // ---------------------------------------------------------------
    // TransitionTo — tillåtna övergångar
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Submitted_WhenDraft_ReturnsSuccess()
    {
        var application = CreateValidApplication();

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionTo_Acknowledged_WhenSubmitted_ReturnsSuccess()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = application.TransitionTo(ApplicationStatus.Acknowledged, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Acknowledged);
    }

    [Fact]
    public void TransitionTo_Submitted_WhenGhosted_ReturnsSuccess()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.MarkGhosted(Clock);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    // ---------------------------------------------------------------
    // TransitionTo — blockerade övergångar (terminaltillstånd)
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_WhenAccepted_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);

        var result = application.TransitionTo(ApplicationStatus.Rejected, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.InvalidTransition");
    }

    [Fact]
    public void TransitionTo_WhenRejected_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Rejected);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.InvalidTransition");
    }

    [Fact]
    public void TransitionTo_WhenWithdrawn_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Withdrawn);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.InvalidTransition");
    }

    [Fact]
    public void TransitionTo_Ghosted_WhenSubmitted_ReturnsFailure()
    {
        // Ghosted är ett automatiskt tillstånd — inte i AllowedTransitions för manuella transitions
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = application.TransitionTo(ApplicationStatus.Ghosted, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.InvalidTransition");
    }

    // ---------------------------------------------------------------
    // TransitionTo — domain event
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_WhenValid_RaisesApplicationStatusTransitionedDomainEvent()
    {
        var application = CreateValidApplication();
        application.ClearDomainEvents();

        application.TransitionTo(ApplicationStatus.Submitted, Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationStatusTransitionedDomainEvent>();
        evt.Previous.ShouldBe(ApplicationStatus.Draft);
        evt.Next.ShouldBe(ApplicationStatus.Submitted);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void TransitionTo_WhenValid_UpdatesUpdatedAt()
    {
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        var application = CreateValidApplication();

        application.TransitionTo(ApplicationStatus.Submitted, laterClock);

        application.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // MarkGhosted
    // ---------------------------------------------------------------

    [Fact]
    public void MarkGhosted_WhenSubmitted_TransitionsToGhosted()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public void MarkGhosted_WhenAcknowledged_TransitionsToGhosted()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.TransitionTo(ApplicationStatus.Acknowledged, Clock);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public void MarkGhosted_WhenSubmitted_RaisesApplicationGhostedDomainEvent()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.ClearDomainEvents();

        application.MarkGhosted(Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationGhostedDomainEvent>();
        evt.Previous.ShouldBe(ApplicationStatus.Submitted);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void MarkGhosted_WhenAccepted_IsIdempotentAndDoesNotChangeStatus()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Accepted);
        application.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MarkGhosted_WhenRejected_IsIdempotentAndDoesNotChangeStatus()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Rejected);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Rejected);
        application.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MarkGhosted_WhenWithdrawn_IsIdempotentAndDoesNotChangeStatus()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Withdrawn);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Withdrawn);
        application.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MarkGhosted_WhenAlreadyGhosted_IsIdempotentAndDoesNotChangeStatus()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.MarkGhosted(Clock);
        application.ClearDomainEvents();

        var result = application.MarkGhosted(Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
        application.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // AddFollowUp — tillåtna tillstånd
    // ---------------------------------------------------------------

    [Fact]
    public void AddFollowUp_WhenDraft_ReturnsSuccess()
    {
        var application = CreateValidApplication();
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Email, scheduledAt, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps.Count.ShouldBe(1);
    }

    [Fact]
    public void AddFollowUp_WhenSubmitted_ReturnsSuccess()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.LinkedIn, scheduledAt, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps.Count.ShouldBe(1);
    }

    [Fact]
    public void AddFollowUp_WhenSubmitted_RaisesFollowUpAddedDomainEvent()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.ClearDomainEvents();
        var scheduledAt = Clock.UtcNow.AddDays(3);

        application.AddFollowUp(FollowUpChannel.Email, scheduledAt, "Hörde av mig via email", Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<FollowUpAddedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // AddFollowUp — blockerade tillstånd (terminaltillstånd)
    // ---------------------------------------------------------------

    [Fact]
    public void AddFollowUp_WhenAccepted_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Email, scheduledAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
    }

    [Fact]
    public void AddFollowUp_WhenRejected_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Rejected);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Phone, scheduledAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
    }

    [Fact]
    public void AddFollowUp_WhenWithdrawn_ReturnsFailure()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Withdrawn);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Other, scheduledAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
    }

    [Fact]
    public void AddFollowUp_WhenGhosted_ReturnsFailure()
    {
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.MarkGhosted(Clock);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Email, scheduledAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
    }

    [Fact]
    public void AddFollowUp_WithNoteExceedingMaxLength_ReturnsFailure()
    {
        var application = CreateValidApplication();
        var tooLongNote = new string('A', 2001);
        var scheduledAt = Clock.UtcNow.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Email, scheduledAt, tooLongNote, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.NoteTooLong");
    }

    // ---------------------------------------------------------------
    // SoftDelete
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_SetsDeletedAt()
    {
        var application = CreateValidApplication();

        application.SoftDelete(Clock);

        application.DeletedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void SoftDelete_AlsoCascadesToFollowUps()
    {
        var application = CreateValidApplication();
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), null, Clock);

        application.SoftDelete(Clock);

        application.FollowUps.ShouldAllBe(f => f.DeletedAt.HasValue);
    }

    [Fact]
    public void SoftDelete_AlsoCascadesToNotes()
    {
        var application = CreateValidApplication();
        application.AddNote("Viktig notering", Clock);

        application.SoftDelete(Clock);

        application.Notes.ShouldAllBe(n => n.DeletedAt.HasValue);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateValidApplication() =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, Clock).Value;

    /// <summary>
    /// Bygger en Application vars Status är satt till <paramref name="target"/>.
    /// Använder en intern statusväg för att nå terminaltillstånd utan
    /// att exponera privat state.
    /// </summary>
    private static Application CreateApplicationAtStatus(ApplicationStatus target)
    {
        var app = Application.Create(ValidJobSeekerId, ValidJobAdId, null, Clock).Value;

        // Draft → Submitted
        if (target == ApplicationStatus.Draft) return app;
        app.TransitionTo(ApplicationStatus.Submitted, Clock);

        if (target == ApplicationStatus.Submitted) return app;

        // Submitted → Rejected / Withdrawn
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

        // Submitted → Acknowledged
        app.TransitionTo(ApplicationStatus.Acknowledged, Clock);
        if (target == ApplicationStatus.Acknowledged) return app;

        // Acknowledged → InterviewScheduled
        app.TransitionTo(ApplicationStatus.InterviewScheduled, Clock);
        if (target == ApplicationStatus.InterviewScheduled) return app;

        // InterviewScheduled → Interviewing
        app.TransitionTo(ApplicationStatus.Interviewing, Clock);
        if (target == ApplicationStatus.Interviewing) return app;

        // Interviewing → OfferReceived
        app.TransitionTo(ApplicationStatus.OfferReceived, Clock);
        if (target == ApplicationStatus.OfferReceived) return app;

        // OfferReceived → Accepted
        if (target == ApplicationStatus.Accepted)
        {
            app.TransitionTo(ApplicationStatus.Accepted, Clock);
            return app;
        }

        throw new InvalidOperationException($"Okänt målstatus: {target}");
    }
}
