using System.Reflection;
using Jobbliggaren.Domain.Common;
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
        var result = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(ValidUserId);
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void Register_WithValidData_RaisesJobSeekerRegisteredEvent()
    {
        var result = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        var events = result.Value.DomainEvents;
        events.ShouldHaveSingleItem();
        var evt = events.Single().ShouldBeOfType<JobSeekerRegisteredDomainEvent>();
        evt.UserId.ShouldBe(ValidUserId);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Register_WithEmptyUserId_Fails()
    {
        var result = JobSeeker.Register(Guid.Empty, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
    }

    [Fact]
    public void SoftDelete_WhenActive_RaisesJobSeekerDeletedDomainEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        seeker.SoftDelete(Clock);
        seeker.ClearDomainEvents();

        seeker.SoftDelete(Clock);

        seeker.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Register_CreatesDefaultPreferences()
    {
        var result = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Preferences.Language.ShouldBe("sv");
        // TD-115: legacy EmailNotifications/WeeklySummary retired; the Vag 4 consent
        // (the live notification model) defaults OFF (GDPR Art. 7 opt-in).
        result.Value.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // F6 Prompt 3 — PrimaryResumeId (ADR 0058 + senior-cto-advisor Alt A2)
    // ---------------------------------------------------------------

    [Fact]
    public void SetPrimaryResume_FromNull_SetsAndRaisesEventAndUpdatesTimestamp()
    {
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

        var result = seeker.SetPrimaryResume(default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.PrimaryResumeIdRequired");
    }

    [Fact]
    public void SetPrimaryResume_SameResumeId_IsIdempotentNoEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        var initialUpdatedAt = seeker.UpdatedAt;
        seeker.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.UnsetPrimaryResume(laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.PrimaryResumeId.ShouldBeNull();
        seeker.UpdatedAt.ShouldBe(initialUpdatedAt);
        seeker.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // #1736 (ADR 0142 D6) — the terms-acceptance stamp. The aggregate is constructible only WITH
    // one (the acceptance-less Register signature was replaced, not overloaded), and the stamp is
    // written once, in the private constructor, and never rewritten.
    // ---------------------------------------------------------------

    [Fact]
    public void Register_WithNullTermsAcceptance_Fails()
    {
        // `null!` is the point of the test, not a convenience: the parameter is non-nullable, and
        // the aggregate refuses at runtime anyway because NRT is not a runtime guarantee. Delete
        // the guard and this is the only test that notices.
        var result = JobSeeker.Register(ValidUserId, null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.TermsAcceptanceRequired");
    }

    [Fact]
    public void Register_WithEmptyUserIdAndNullTermsAcceptance_ReportsTheUserIdFailure()
    {
        // Guard ORDER, not merely guard presence. Swapping the two guards leaves every other test
        // in this file green, so the ordering needs a request that is wrong in both ways.
        var result = JobSeeker.Register(Guid.Empty, null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
    }

    [Fact]
    public void Register_WithValidData_CarriesTheTermsAcceptanceItWasGiven()
    {
        var acceptance = TermsAcceptance.AcceptCurrent(Clock);

        var result = JobSeeker.Register(ValidUserId, acceptance, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TermsAcceptance.ShouldBe(acceptance);
    }

    [Fact]
    public void Register_RaisesTheEventWithItsExistingFields_AndNoTermsVersions()
    {
        // The ROW is the Art. 5(2) accountability record; the event only announces that a
        // registration happened. Pinned reflectively so adding a version to the event — a second
        // home for the same fact, and one no consumer reads — is a deliberate edit here.
        var seeker = JobSeeker
            .Register(ValidUserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobSeekerRegisteredDomainEvent>();

        evt.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["JobSeekerId", "OccurredAt", "UserId"]);
    }

    [Fact]
    public void TermsAcceptance_HasNoPublicWritePathOnTheAggregate()
    {
        // Two arms, each catching a different mutation.
        //
        // (1) The `private set` relaxed to a public one: the stamp becomes assignable by any caller
        // holding the aggregate, so a registered acceptance could be back-dated or re-versioned.
        var setter = typeof(JobSeeker).GetProperty(nameof(JobSeeker.TermsAcceptance))!.SetMethod;
        setter.ShouldNotBeNull();
        setter.IsPublic.ShouldBeFalse();

        // (2) A new INSTANCE method taking a TermsAcceptance — an `UpdateTermsAcceptance`. The stamp
        // is written once, in the constructor Register calls, so the only member carrying this type
        // is the static factory. A setter kept private while such a method appears would pass arm
        // (1) alone.
        typeof(JobSeeker)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(TermsAcceptance)))
            .Select(m => m.Name)
            .ShouldBeEmpty();

        // (3) A re-acceptance path that builds the stamp inside — `ReAcceptTerms(clock)` — takes no
        // TermsAcceptance and passes arms (1) and (2). Caught by name: no public instance method of
        // the aggregate names the terms (the property's own accessor is a special name and excluded).
        // Re-acceptance is append-only by ADR 0142 D6's amendment, never an in-place rewrite, so a
        // method here would be the wrong home by construction.
        typeof(JobSeeker)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.Name.Contains("Terms", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ShouldBeEmpty();
    }

    [Fact]
    public void DisplayName_HasNoWritePathOnTheAggregate()
    {
        // ADR 0142 D7: the account has no name, and from #1742 on the model does not map the column.
        // This is the pin the test seam writing a legacy name (tests/Shared/LegacyAccountName.cs) names:
        // no current writer can produce the state, so a fixture carrying a name asserts about a row a
        // retired actor wrote.
        //
        // (1) The factory taking a name again, required or optional: its parameters are exactly these.
        typeof(JobSeeker)
            .GetMethod(nameof(JobSeeker.Register), BindingFlags.Public | BindingFlags.Static)!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe([typeof(Guid), typeof(TermsAcceptance), typeof(IDateTimeProvider)]);

        // (2) Any public member that names it: a property, an `UpdateDisplayName`, a validator, a
        // length cap.
        typeof(JobSeeker)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name.Contains("DisplayName", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ShouldBeEmpty();
    }
}
