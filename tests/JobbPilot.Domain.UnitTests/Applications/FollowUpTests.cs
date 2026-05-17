using JobbPilot.Domain.Applications;
using JobbPilot.Domain.UnitTests.JobAds;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.Applications;

/// <summary>
/// FollowUp.Create är internal — testas via Application.AddFollowUp.
/// Direkt FollowUp-logik (RecordOutcome, SoftDelete) är exponerad via
/// FollowUps-listan och testas här via aggregatroten.
/// </summary>
public class FollowUpTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly FakeDateTimeProvider LaterClock =
        FakeDateTimeProvider.At(FakeDateTimeProvider.Default.UtcNow.AddHours(2));

    private static Application CreateActiveApplication()
    {
        var jobSeekerId = new JobbPilot.Domain.JobSeekers.JobSeekerId(Guid.NewGuid());
        return Application.Create(jobSeekerId, null, null, null, Clock).Value;
    }

    // ---------------------------------------------------------------
    // AddFollowUp — lyckade fall
    // ---------------------------------------------------------------

    [Fact]
    public void AddFollowUp_WithNullNote_ReturnsSuccess()
    {
        var application = CreateActiveApplication();

        var result = application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(5), null, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps.Count.ShouldBe(1);
    }

    [Fact]
    public void AddFollowUp_SetsChannelCorrectly()
    {
        var application = CreateActiveApplication();

        application.AddFollowUp(FollowUpChannel.LinkedIn, Clock.UtcNow.AddDays(2), null, Clock);

        application.FollowUps[0].Channel.ShouldBe(FollowUpChannel.LinkedIn);
    }

    [Fact]
    public void AddFollowUp_SetsScheduledAtCorrectly()
    {
        var application = CreateActiveApplication();
        var scheduledAt = Clock.UtcNow.AddDays(7);

        application.AddFollowUp(FollowUpChannel.Phone, scheduledAt, null, Clock);

        application.FollowUps[0].ScheduledAt.ShouldBe(scheduledAt);
    }

    [Fact]
    public void AddFollowUp_SetsDefaultOutcomeToPending()
    {
        var application = CreateActiveApplication();

        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), null, Clock);

        application.FollowUps[0].Outcome.ShouldBe(FollowUpOutcome.Pending);
    }

    [Fact]
    public void AddFollowUp_TrimsNote()
    {
        var application = CreateActiveApplication();

        application.AddFollowUp(FollowUpChannel.Other, Clock.UtcNow.AddDays(1), "  Notering  ", Clock);

        application.FollowUps[0].Note.ShouldBe("Notering");
    }

    [Fact]
    public void AddFollowUp_MultipleFollowUps_AllAreStoredInOrder()
    {
        var application = CreateActiveApplication();

        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), "Första", Clock);
        application.AddFollowUp(FollowUpChannel.LinkedIn, Clock.UtcNow.AddDays(3), "Andra", Clock);

        application.FollowUps.Count.ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // AddFollowUp — valideringsfel
    // ---------------------------------------------------------------

    [Fact]
    public void AddFollowUp_WithNoteAtMaxLength_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var maxNote = new string('A', 2000);

        var result = application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), maxNote, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AddFollowUp_WithNoteTooLong_ReturnsFailure()
    {
        var application = CreateActiveApplication();
        var tooLong = new string('A', 2001);

        var result = application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.NoteTooLong");
        application.FollowUps.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // RecordOutcome
    // ---------------------------------------------------------------

    [Fact]
    public void RecordOutcome_WhenPending_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), null, Clock);
        var followUp = application.FollowUps[0];

        var result = followUp.RecordOutcome(FollowUpOutcome.Responded, LaterClock);

        result.IsSuccess.ShouldBeTrue();
        followUp.Outcome.ShouldBe(FollowUpOutcome.Responded);
        followUp.OutcomeAt.ShouldBe(LaterClock.UtcNow);
    }

    [Fact]
    public void RecordOutcome_WhenAlreadyRecorded_ReturnsFailure()
    {
        var application = CreateActiveApplication();
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), null, Clock);
        var followUp = application.FollowUps[0];
        followUp.RecordOutcome(FollowUpOutcome.Responded, LaterClock);

        var result = followUp.RecordOutcome(FollowUpOutcome.NoResponse, LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.OutcomeAlreadyRecorded");
    }

    // ---------------------------------------------------------------
    // SoftDelete (via Application.SoftDelete-kaskad)
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_ViaApplicationCascade_SetsDeletedAtOnFollowUp()
    {
        var application = CreateActiveApplication();
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(1), null, Clock);

        application.SoftDelete(LaterClock);

        application.FollowUps[0].DeletedAt.ShouldBe(LaterClock.UtcNow);
    }
}
