using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

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
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock);

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
        var result = Application.Create(ValidJobSeekerId, null, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobAdId.ShouldBeNull();
    }

    [Fact]
    public void Create_WithDefaultJobSeekerId_ReturnsFailure()
    {
        var result = Application.Create(default, ValidJobAdId, null, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobSeekerIdRequired");
    }

    [Fact]
    public void Create_WithCoverLetterAtMaxLength_ReturnsSuccess()
    {
        var coverLetter = new string('A', 10_000);

        var result = Application.Create(ValidJobSeekerId, null, coverLetter, null, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithCoverLetterExceedingMaxLength_ReturnsFailure()
    {
        var tooLong = new string('A', 10_001);

        var result = Application.Create(ValidJobSeekerId, null, tooLong, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.CoverLetterTooLong");
    }

    [Fact]
    public void Create_TrimsCoverLetter()
    {
        var result = Application.Create(ValidJobSeekerId, null, "  Hej  ", null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CoverLetter.ShouldBe("Hej");
    }

    // ---------------------------------------------------------------
    // Create — domain event
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithValidData_RaisesApplicationCreatedDomainEvent()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock);

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
        var result = Application.Create(default, ValidJobAdId, null, null, Clock);

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
    // TransitionTo — fria övergångar (ADR 0092 D3): terminaltillstånd kan
    // återöppnas och manuell Ghosted är tillåten. Djupare assertions (timeline,
    // event-typ, snapshot-minimering) ligger i ApplicationFreeTransitionTests.
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ReopenFromAccepted_ReturnsSuccess()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Accepted);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionTo_ReopenFromRejected_ReturnsSuccess()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Rejected);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionTo_ReopenFromWithdrawn_ReturnsSuccess()
    {
        var application = CreateApplicationAtStatus(ApplicationStatus.Withdrawn);

        var result = application.TransitionTo(ApplicationStatus.Submitted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionTo_ManualGhosted_WhenSubmitted_ReturnsSuccess()
    {
        // ADR 0092 D3: manuell Ghosted går nu via TransitionTo (inte enbart MarkGhosted).
        var application = CreateValidApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = application.TransitionTo(ApplicationStatus.Ghosted, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
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

    [Fact]
    public void SoftDelete_WhenActive_RaisesApplicationDeletedDomainEvent()
    {
        var application = CreateValidApplication();
        application.ClearDomainEvents();

        application.SoftDelete(Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationDeletedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.JobSeekerId.ShouldBe(application.JobSeekerId);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotentAndDoesNotRaiseEvent()
    {
        var application = CreateValidApplication();
        application.SoftDelete(Clock);
        application.ClearDomainEvents();

        application.SoftDelete(Clock);

        application.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // LastStatusChangeAt + GhostedThresholdDays (Fas 9.1)
    // ---------------------------------------------------------------

    [Fact]
    public void Application_Create_SetsLastStatusChangeAtToCurrentTime()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var clock = FakeDateTimeProvider.At(t1);

        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LastStatusChangeAt.ShouldBe(t1);
    }

    [Fact]
    public void Application_Create_SetsGhostedThresholdDaysToDefault21()
    {
        var result = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.GhostedThresholdDays.ShouldBe(21);
    }

    [Fact]
    public void Application_TransitionTo_UpdatesLastStatusChangeAt()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;

        var result = application.TransitionTo(ApplicationStatus.Submitted, clockT2);

        result.IsSuccess.ShouldBeTrue();
        application.LastStatusChangeAt.ShouldBe(t2);
    }

    [Fact]
    public void Application_TransitionTo_OnDeletedApplication_ReturnsFailureAndDoesNotUpdateLastStatusChangeAt()
    {
        // ADR 0092 D3: fria övergångar tog bort state-machine-guarden. Den enda
        // kvarvarande Failure-vägen är en soft-deletad ansökan (en self-transition
        // är i stället en no-op-Success). En avvisad övergång får inte flytta
        // LastStatusChangeAt.
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var clockT3 = FakeDateTimeProvider.At(t3);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;
        application.SoftDelete(clockT2);

        var result = application.TransitionTo(ApplicationStatus.Submitted, clockT3);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.DeletedCannotTransition");
        application.LastStatusChangeAt.ShouldBe(t1);
    }

    [Fact]
    public void Application_MarkGhosted_UpdatesLastStatusChangeAt()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var t3 = t2.AddDays(22);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var clockT3 = FakeDateTimeProvider.At(t3);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;
        application.TransitionTo(ApplicationStatus.Submitted, clockT2);

        var result = application.MarkGhosted(clockT3);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
        application.LastStatusChangeAt.ShouldBe(t3);
    }

    [Fact]
    public void Application_MarkGhosted_FromTerminalState_DoesNotUpdateLastStatusChangeAt()
    {
        // Status=Draft → MarkGhosted är idempotent success utan flip enligt aggregatets nuvarande logik.
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;

        var result = application.MarkGhosted(clockT2);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Draft);
        application.LastStatusChangeAt.ShouldBe(t1);
    }

    [Fact]
    public void Application_AddFollowUp_DoesNotUpdateLastStatusChangeAt()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;
        var scheduledAt = t2.AddDays(3);

        var result = application.AddFollowUp(FollowUpChannel.Email, scheduledAt, null, clockT2);

        result.IsSuccess.ShouldBeTrue();
        application.LastStatusChangeAt.ShouldBe(t1);
    }

    [Fact]
    public void Application_AddNote_DoesNotUpdateLastStatusChangeAt()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;

        var result = application.AddNote("Viktig notering", clockT2);

        result.IsSuccess.ShouldBeTrue();
        application.LastStatusChangeAt.ShouldBe(t1);
    }

    [Fact]
    public void Application_SoftDelete_DoesNotUpdateLastStatusChangeAt()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddMinutes(5);
        var clockT1 = FakeDateTimeProvider.At(t1);
        var clockT2 = FakeDateTimeProvider.At(t2);
        var application = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clockT1).Value;

        application.SoftDelete(clockT2);

        application.DeletedAt.ShouldBe(t2);
        application.LastStatusChangeAt.ShouldBe(t1);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateValidApplication() =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock).Value;

    /// <summary>
    /// Bygger en Application vars Status är satt till <paramref name="target"/>.
    /// Använder en intern statusväg för att nå terminaltillstånd utan
    /// att exponera privat state.
    /// </summary>
    private static Application CreateApplicationAtStatus(ApplicationStatus target)
    {
        var app = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock).Value;

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
