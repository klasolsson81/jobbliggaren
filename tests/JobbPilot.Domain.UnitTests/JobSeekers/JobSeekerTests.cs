using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.JobSeekers.Events;
using JobbPilot.Domain.UnitTests.JobAds;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.JobSeekers;

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
}
