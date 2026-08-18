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

    // ---------------------------------------------------------------
    // #1117 (CLAUDE.md §5 — the highest-priority PII rule): the aggregate REFUSES a
    // personnummer-shaped display name on BOTH write paths (Register / UpdateDisplayName).
    // DisplayName is a plaintext, unencrypted column that surfaces on screen, in the profile
    // DTO, and — via PersonalInfo.FullName on the promote path — in the PDF header the user
    // sends to employers. Same invariant, same flag chain (Normalize -> Scan) and same
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
        var result = JobSeeker.Register(ValidUserId, pnrName, Clock);

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
        var result = JobSeeker.Register(ValidUserId, "811218-9875", Clock);

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
        var result = JobSeeker.Register(ValidUserId, "  Anna Andersson  ", Clock);

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

        var result = JobSeeker.Register(ValidUserId, exactly200, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.Length.ShouldBe(200);
    }

    [Theory]
    [InlineData("811218-9876")]
    [InlineData("8112189876")]
    [InlineData("811278-9873")] // samordningsnummer
    [InlineData("19811218-9876")] // 12-digit century form
    [InlineData("811218\u00A09876")] // NBSP-gapped: proves Normalize runs before Scan
    [InlineData("Anna 811218-9876")]
    public void UpdateDisplayName_WithPersonnummerShapedDisplayName_ReturnsFailure(string pnrName)
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var before = seeker.DisplayName;
        var beforeUpdatedAt = seeker.UpdatedAt;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.UpdateDisplayName(pnrName, laterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNamePersonnummerMustBeRemoved");
        seeker.DisplayName.ShouldBe(before); // refused -> DisplayName is not mutated
        seeker.UpdatedAt.ShouldBe(beforeUpdatedAt); // and the row is not stamped
    }

    [Fact]
    public void UpdateDisplayName_WithPersonnummerLookalikeFailingLuhn_IsAllowed_NoOverFlag()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.UpdateDisplayName("811218-9875", laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.DisplayName.ShouldBe("811218-9875");
    }

    // The two length/blank rules UpdateDisplayName has always carried had no test at all
    // before #1117 (measured: zero UpdateDisplayName tests). They are pinned here because
    // the guard moves them into a shared validator, and an unpinned rule that moves is a
    // rule that can silently stop running.

    [Fact]
    public void UpdateDisplayName_WithBlankDisplayName_Fails()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;

        var result = seeker.UpdateDisplayName("   ", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameRequired");
    }

    [Fact]
    public void UpdateDisplayName_WithTooLongDisplayName_Fails()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        // Literal, deliberately NOT MaxDisplayNameLength + 1: a derived length follows a
        // mutated constant and would stop killing that mutant (parity with Register's case).
        var tooLong = new string('A', 201);

        var result = seeker.UpdateDisplayName(tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameTooLong");
    }

    [Fact]
    public void UpdateDisplayName_TrimsDisplayName()
    {
        var seeker = JobSeeker.Register(ValidUserId, "Klas Olsson", Clock).Value;
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = seeker.UpdateDisplayName("  Anna  ", laterClock);

        result.IsSuccess.ShouldBeTrue();
        seeker.DisplayName.ShouldBe("Anna");
        seeker.UpdatedAt.ShouldBe(laterClock.UtcNow);
    }
}
