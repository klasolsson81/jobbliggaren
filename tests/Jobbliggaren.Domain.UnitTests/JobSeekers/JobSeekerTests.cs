using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.JobSeekers.Events;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

public class JobSeekerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly Guid ValidUserId = Guid.NewGuid();

    [Fact]
    public void Register_WithValidData_CreatesJobSeeker()
    {
        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(ValidUserId);
        result.Value.DisplayName.ShouldBe("Klas Olsson");
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void Register_WithValidData_RaisesJobSeekerRegisteredEvent()
    {
        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock);

        result.IsSuccess.ShouldBeTrue();
        var events = result.Value.DomainEvents;
        events.ShouldHaveSingleItem();
        var evt = events.Single().ShouldBeOfType<JobSeekerRegisteredDomainEvent>();
        evt.UserId.ShouldBe(ValidUserId);
        evt.DisplayName.ShouldBe("Klas Olsson");
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Register_WithEmptyUserId_Fails()
    {
        var result = JobSeeker.Register(Guid.Empty, "Klas", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
    }

    [Fact]
    public void Register_WithBlankDisplayName_Fails()
    {
        var result = JobSeeker.Register(ValidUserId, "   ", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameRequired");
    }

    [Fact]
    public void Register_WithTooLongDisplayName_Fails()
    {
        var tooLong = new string('A', 201);

        var result = JobSeeker.Register(ValidUserId, tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameTooLong");
    }

    [Fact]
    public void Register_TrimsDisplayName()
    {
        var result = JobSeeker.Register(ValidUserId, "  Klas  ", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBe("Klas");
    }

    [Fact]
    public void SoftDelete_WhenActive_RaisesJobSeekerDeletedDomainEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        seeker.ClearDomainEvents();

        seeker.SoftDelete(Clock);

        seeker.DeletedAt.ShouldBe(Clock.UtcNow);
        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobSeekerDeletedDomainEvent>();
        evt.JobSeekerId.ShouldBe(seeker.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_IsIdempotentAndDoesNotRaiseEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        seeker.SoftDelete(Clock);
        seeker.ClearDomainEvents();

        seeker.SoftDelete(Clock);

        seeker.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Register_CreatesDefaultPreferences()
    {
        var result = JobSeeker.Register(ValidUserId, "Klas", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Preferences.Language.ShouldBe("sv");
        result.Value.Preferences.EmailNotifications.ShouldBeTrue();
        result.Value.Preferences.WeeklySummary.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // F6 Prompt 3 — PrimaryResumeId (ADR 0058 + senior-cto-advisor Alt A2)
    // ---------------------------------------------------------------

    [Fact]
    public void SetPrimaryResume_FromNull_SetsAndRaisesEventAndUpdatesTimestamp()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;
        seeker.ClearDomainEvents();
        var resumeId = ResumeId.New();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.SetPrimaryResume(resumeId, laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBe(resumeId);
        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<PrimaryResumeSetDomainEvent>();
        evt.JobSeekerId.ShouldBe(seeker.Id);
        evt.NewPrimaryResumeId.ShouldBe(resumeId);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void SetPrimaryResume_OverwritePrevious_RaisesEventWithNewId()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;
        var firstResume = ResumeId.New();
        var secondResume = ResumeId.New();
        seeker.SetPrimaryResume(firstResume, Clock);
        seeker.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));

        var result = seeker.SetPrimaryResume(secondResume, laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBe(secondResume);
        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<PrimaryResumeSetDomainEvent>();
        evt.NewPrimaryResumeId.ShouldBe(secondResume);
    }

    [Fact]
    public void SetPrimaryResume_DefaultGuid_ReturnsValidationFailure()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;

        var result = seeker.SetPrimaryResume(default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.PrimaryResumeIdRequired");
    }

    [Fact]
    public void SetPrimaryResume_SameResumeId_IsIdempotentNoEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;
        var resumeId = ResumeId.New();
        seeker.SetPrimaryResume(resumeId, Clock);
        var prevUpdatedAt = seeker.UpdatedAt;
        seeker.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(3));

        var result = seeker.SetPrimaryResume(resumeId, laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBe(resumeId);
        seeker.UpdatedAt.ShouldBe(prevUpdatedAt);
        seeker.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void UnsetPrimaryResume_FromSet_NullifiesAndRaisesEventWithNull()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;
        seeker.SetPrimaryResume(ResumeId.New(), Clock);
        seeker.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));

        var result = seeker.UnsetPrimaryResume(laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<PrimaryResumeSetDomainEvent>();
        evt.JobSeekerId.ShouldBe(seeker.Id);
        evt.NewPrimaryResumeId.ShouldBeNull();
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void UnsetPrimaryResume_AlreadyNull_IsIdempotent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", Clock).Value;
        var initialUpdatedAt = seeker.UpdatedAt;
        seeker.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.UnsetPrimaryResume(laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(initialUpdatedAt);
        seeker.DomainEvents.ShouldBeEmpty();
    }
}
