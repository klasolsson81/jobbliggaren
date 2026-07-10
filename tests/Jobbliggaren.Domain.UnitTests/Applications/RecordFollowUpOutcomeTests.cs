using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

/// <summary>
/// RecordFollowUpOutcome-vertikalen (FAS 3 in-block, arkitekt-beslut
/// a1adb06cf1d1e8155). Domänmetoden Application.RecordFollowUpOutcome
/// slår upp i privata _followUps, delegerar till FollowUp.RecordOutcome
/// och raisar FollowUpOutcomeRecordedDomainEvent vid success.
///
/// RÖD tills Application.RecordFollowUpOutcome +
/// FollowUpOutcomeRecordedDomainEvent implementerats.
///
/// Beslut 4 (KRITISKT regressionsskydd): RecordFollowUpOutcome får INTE
/// blockeras när ansökan är Ghosted/stängd — till skillnad från AddFollowUp.
/// </summary>
public class RecordFollowUpOutcomeTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly FakeDateTimeProvider LaterClock =
        FakeDateTimeProvider.At(FakeDateTimeProvider.Default.UtcNow.AddHours(2));

    private static Application CreateActiveApplication()
    {
        var jobSeekerId = new JobSeekerId(Guid.NewGuid());
        return Application.Create(jobSeekerId, null, null, null, Clock).Value;
    }

    private static FollowUpId AddPendingFollowUp(Application application)
    {
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(3), null, Clock);
        return application.FollowUps[^1].Id;
    }

    // ---------------------------------------------------------------
    // Success
    // ---------------------------------------------------------------

    [Fact]
    public void RecordFollowUpOutcome_WhenPending_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenPending_SetsOutcomeAndOutcomeAt()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);

        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        var followUp = application.FollowUps.Single(f => f.Id == followUpId);
        followUp.Outcome.ShouldBe(FollowUpOutcome.Responded);
        followUp.OutcomeAt.ShouldBe(LaterClock.UtcNow);
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenSuccess_RaisesFollowUpOutcomeRecordedDomainEvent()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.ClearDomainEvents();

        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<FollowUpOutcomeRecordedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.FollowUpId.ShouldBe(followUpId);
        evt.Outcome.ShouldBe(FollowUpOutcome.Responded);
        evt.OccurredAt.ShouldBe(LaterClock.UtcNow);
    }

    // ---------------------------------------------------------------
    // Beslut 4 — regressionsskydd: Ghosted ansökan får registrera utfall
    // ---------------------------------------------------------------

    [Fact]
    public void RecordFollowUpOutcome_WhenApplicationGhosted_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.TransitionTo(ApplicationStatus.Submitted, Clock);
        application.TransitionTo(ApplicationStatus.Ghosted, Clock);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.NoResponse, LaterClock);

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Ghosted);
        application.FollowUps.Single(f => f.Id == followUpId).Outcome.ShouldBe(FollowUpOutcome.NoResponse);
    }

    // ---------------------------------------------------------------
    // FollowUp saknas → NotFound
    // ---------------------------------------------------------------

    [Fact]
    public void RecordFollowUpOutcome_WhenFollowUpDoesNotExist_ReturnsNotFoundFailure()
    {
        var application = CreateActiveApplication();
        AddPendingFollowUp(application);

        var result = application.RecordFollowUpOutcome(
            new FollowUpId(Guid.NewGuid()), FollowUpOutcome.Responded, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotFound");
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenNoFollowUps_DoesNotRaiseEvent()
    {
        var application = CreateActiveApplication();
        application.ClearDomainEvents();

        application.RecordFollowUpOutcome(
            new FollowUpId(Guid.NewGuid()), FollowUpOutcome.Responded, LaterClock);

        application.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenFollowUpSoftDeleted_TreatedAsNotFound()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        // SoftDelete-kaskad sätter DeletedAt på follow-up:en.
        application.SoftDelete(Clock);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotFound");
    }

    // ---------------------------------------------------------------
    // Redan registrerat utfall → Conflict (idempotens/dubbelregistrering)
    // ---------------------------------------------------------------

    [Fact]
    public void RecordFollowUpOutcome_WhenOutcomeAlreadyRecorded_ReturnsConflictFailure()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.NoResponse, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.OutcomeAlreadyRecorded");
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenOutcomeAlreadyRecorded_DoesNotRaiseSecondEvent()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);
        application.ClearDomainEvents();

        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.NoResponse, LaterClock);

        application.DomainEvents.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // #644 — the recorded outcome must be a resolution (Responded/NoResponse);
    // Pending (initial) and Logged (creation-only) are rejected targets.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(nameof(FollowUpOutcome.Pending))]
    [InlineData(nameof(FollowUpOutcome.Logged))]
    public void RecordFollowUpOutcome_WithNonResolutionTarget_ReturnsValidationFailure(string outcomeName)
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        var outcome = FollowUpOutcome.FromName(outcomeName);

        var result = application.RecordFollowUpOutcome(followUpId, outcome, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.InvalidOutcome");
    }

    [Fact]
    public void RecordFollowUpOutcome_WithNonResolutionTarget_LeavesFollowUpPending()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);

        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Logged, LaterClock);

        var followUp = application.FollowUps.Single(f => f.Id == followUpId);
        followUp.Outcome.ShouldBe(FollowUpOutcome.Pending);
        followUp.OutcomeAt.ShouldBeNull();
    }

    [Fact]
    public void RecordFollowUpOutcome_WithNonResolutionTarget_DoesNotRaiseEvent()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.ClearDomainEvents();

        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Pending, LaterClock);

        application.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void RecordFollowUpOutcome_WithNoResponse_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.NoResponse, LaterClock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps.Single(f => f.Id == followUpId).Outcome.ShouldBe(FollowUpOutcome.NoResponse);
    }

    [Fact]
    public void RecordFollowUpOutcome_WhenAlreadyRecordedAndTargetDisallowed_ConflictWinsOverInvalidOutcome()
    {
        // Pins the guard ORDER (#644): an already-resolved follow-up given a disallowed target hits
        // the Conflict check first — a reorder would silently flip 409→400 (test-writer PR B).
        var application = CreateActiveApplication();
        var followUpId = AddPendingFollowUp(application);
        application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Responded, LaterClock);

        var result = application.RecordFollowUpOutcome(followUpId, FollowUpOutcome.Pending, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.OutcomeAlreadyRecorded");
    }
}
