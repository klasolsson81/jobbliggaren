using System.Reflection;
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
        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(ValidUserId);
        result.Value.DisplayName.ShouldBe("Klas Olsson");
        result.Value.CreatedAt.ShouldBe(Clock.UtcNow);
        result.Value.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void Register_WithValidData_RaisesJobSeekerRegisteredEvent()
    {
        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", TermsAcceptance.AcceptCurrent(Clock), Clock);

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
        var result = JobSeeker.Register(Guid.Empty, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
    }

    // ADR 0142 D7, the expand half (#1737): a passwordless account is created before anyone has typed a
    // name, so the aggregate admits an absent one. A name that IS given still meets every rule.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutADisplayName_CreatesASeekerWithNoName(string? absent)
    {
        var result = JobSeeker.Register(ValidUserId, absent, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBeNull();
        result.Value.DomainEvents.Single().ShouldBeOfType<JobSeekerRegisteredDomainEvent>()
            .DisplayName.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateDisplayName_StillRefusesAnAbsentName(string? absent)
    {
        // RegisterCommandHandler and UpdateDisplayName both require a name and both go through here.
        var result = JobSeeker.ValidateDisplayName(absent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameRequired");
    }

    [Fact]
    public void Register_WithTooLongDisplayName_Fails()
    {
        var tooLong = new string('A', 201);

        var result = JobSeeker.Register(ValidUserId, tooLong, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameTooLong");
    }

    [Fact]
    public void Register_TrimsDisplayName()
    {
        var result = JobSeeker.Register(ValidUserId, "  Klas  ", TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBe("Klas");
    }

    [Fact]
    public void SoftDelete_WhenActive_RaisesJobSeekerDeletedDomainEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        seeker.SoftDelete(Clock);
        seeker.ClearDomainEvents();

        seeker.SoftDelete(Clock);

        seeker.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Register_CreatesDefaultPreferences()
    {
        var result = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock);

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
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

        var result = seeker.SetPrimaryResume(default, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.PrimaryResumeIdRequired");
    }

    [Fact]
    public void SetPrimaryResume_SameResumeId_IsIdempotentNoEvent()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
        var seeker = JobSeeker.Register(ValidUserId, "Klas", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
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
    // #1117 (CLAUDE.md §5 — the highest-priority PII rule): the aggregate REFUSES a
    // personnummer-shaped display name on BOTH write paths (Register / UpdateDisplayName).
    // DisplayName is a plaintext, unencrypted column that surfaces on screen and in the profile
    // DTO. Same invariant, same flag chain (Normalize -> Scan) and same
    // date+Luhn authority as Resume.ValidateName, whose written justification applies
    // verbatim here. This is also the pin the seams that seed a legacy display name name:
    // the CURRENT writers cannot produce the shape, so a fixture carrying one is asserting
    // about rows written before this invariant landed.
    // Non-ASCII gap points as \uXXXX escapes (project rule: ASCII source).
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("811218-9876")] // valid 10-digit personnummer
    [InlineData("8112189876")] // contiguous, no separator
    [InlineData("811278-9873")] // samordningsnummer (day 18+60=78)
    [InlineData("19811218-9876")] // 12-digit century form
    [InlineData("198112189876")] // 12-digit century form, contiguous
    [InlineData("811218\u00A09876")] // NBSP-gapped: proves Normalize runs before Scan
    [InlineData("Anna 811218-9876")] // embedded in an otherwise ordinary name
    public void Register_WithPersonnummerShapedDisplayName_ReturnsFailure(string pnrName)
    {
        var result = JobSeeker.Register(ValidUserId, pnrName, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNamePersonnummerMustBeRemoved");
    }

    [Fact]
    public void Register_WithPersonnummerLookalikeFailingLuhn_IsAllowed_NoOverFlag()
    {
        // "811218-9875" has the personnummer SHAPE but a wrong Luhn check digit, so it is NOT
        // a personnummer. The date+Luhn authority governs the guard, so it must NOT over-flag
        // — over-flagging refuses a legitimate name, which is the direction that harms a real
        // user. Parity with ResumeTests.Create_WithPersonnummerLookalikeFailingLuhn_IsAllowed_NoOverFlag.
        var result = JobSeeker.Register(ValidUserId, "811218-9875", TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBe("811218-9875");
    }

    [Fact]
    public void Register_RaisesTheEventWithTheValidatedName_NotTheRawInput()
    {
        // The event carries the display name in its payload, so it must carry the value the
        // validator returned rather than the caller's string. Pinned because Register composes
        // the aggregate and the event from one canonical value; regressing to the raw argument
        // would put an untrimmed name on the wire the day a dispatcher exists.
        var result = JobSeeker.Register(ValidUserId, "  Anna Andersson  ", TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        var evt = result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobSeekerRegisteredDomainEvent>();
        evt.DisplayName.ShouldBe("Anna Andersson");
        evt.DisplayName.ShouldBe(result.Value.DisplayName);
    }

    [Fact]
    public void Register_WithExactlyMaxLengthDisplayName_IsAllowed()
    {
        // The boundary itself: a name of exactly the limit is VALID. Without this, relaxing the
        // comparison to >= survives every other length test.
        var exactly200 = new string('A', 200);

        var result = JobSeeker.Register(ValidUserId, exactly200, TermsAcceptance.AcceptCurrent(Clock), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldNotBeNull().Length.ShouldBe(200);
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
        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.TermsAcceptanceRequired");
    }

    [Fact]
    public void Register_WithEmptyUserIdAndNullTermsAcceptance_ReportsTheUserIdFailure()
    {
        // Guard ORDER, not merely guard presence. Swapping the two guards leaves every other test
        // in this file green, so the ordering needs a request that is wrong in both ways.
        var result = JobSeeker.Register(Guid.Empty, "Klas Olsson", null!, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
    }

    [Fact]
    public void Register_WithValidData_CarriesTheTermsAcceptanceItWasGiven()
    {
        var acceptance = TermsAcceptance.AcceptCurrent(Clock);

        var result = JobSeeker.Register(ValidUserId, "Klas Olsson", acceptance, Clock);

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
            .Register(ValidUserId, "Klas Olsson", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

        var evt = seeker.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobSeekerRegisteredDomainEvent>();

        evt.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(["DisplayName", "JobSeekerId", "OccurredAt", "UserId"]);
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
}
