using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

/// <summary>
/// ADR 0092 D4/D5 "Logga uppföljning" + the shared LastFollowUpAt wait-reset
/// scalar. <see cref="Application.LogFollowUp"/> creates a completed-contact-today
/// follow-up (channel Other, outcome Logged, scheduled = now) and bumps
/// LastFollowUpAt so the effective wait resets. Mirrors FollowUpTests /
/// ApplicationTests conventions (via the aggregate root; FollowUp.CreateLogged is
/// internal so it is exercised through LogFollowUp).
/// </summary>
public class ApplicationLogFollowUpTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());

    private static Application CreateActiveApplication() =>
        Application.Create(ValidJobSeekerId, null, null, null, Clock).Value;

    // Reaches a closed-for-activity status (the three terminals + Ghosted) from
    // Submitted via free transitions (ADR 0092 D3).
    private static Application ClosedApplication(ApplicationStatus target)
    {
        var app = CreateActiveApplication();
        app.TransitionTo(ApplicationStatus.Submitted, Clock);
        app.TransitionTo(target, Clock);
        return app;
    }

    // ---------------------------------------------------------------
    // LogFollowUp — success
    // ---------------------------------------------------------------

    [Fact]
    public void LogFollowUp_WhenDraft_ReturnsSuccessAndAddsFollowUp()
    {
        var application = CreateActiveApplication();

        var result = application.LogFollowUp("Ringde rekryteraren", Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldNotBe(Guid.Empty);
        application.FollowUps.Count.ShouldBe(1);
    }

    [Fact]
    public void LogFollowUp_WhenSubmitted_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        application.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = application.LogFollowUp("Kontakt", Clock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps.Count.ShouldBe(1);
    }

    [Fact]
    public void LogFollowUp_CreatesLoggedFollowUp_ChannelOther_OutcomeLogged_ScheduledNow()
    {
        var t = new DateTimeOffset(2026, 5, 8, 9, 30, 0, TimeSpan.Zero);
        var clock = FakeDateTimeProvider.At(t);
        var application = Application.Create(ValidJobSeekerId, null, null, null, clock).Value;

        application.LogFollowUp("Kontakt", clock);

        var followUp = application.FollowUps[0];
        followUp.Channel.ShouldBe(FollowUpChannel.Other);
        followUp.Outcome.ShouldBe(FollowUpOutcome.Logged);
        followUp.ScheduledAt.ShouldBe(t);
        followUp.CreatedAt.ShouldBe(t);
        followUp.Note.ShouldBe("Kontakt");
    }

    [Fact]
    public void LogFollowUp_WithNullNote_ReturnsSuccess()
    {
        var application = CreateActiveApplication();

        var result = application.LogFollowUp(null, Clock);

        result.IsSuccess.ShouldBeTrue();
        application.FollowUps[0].Note.ShouldBeNull();
    }

    [Fact]
    public void LogFollowUp_TrimsNote()
    {
        var application = CreateActiveApplication();

        application.LogFollowUp("  Kontakt  ", Clock);

        application.FollowUps[0].Note.ShouldBe("Kontakt");
    }

    [Fact]
    public void LogFollowUp_BumpsLastFollowUpAtToFollowUpCreatedAt()
    {
        var t = new DateTimeOffset(2026, 5, 8, 9, 30, 0, TimeSpan.Zero);
        var clock = FakeDateTimeProvider.At(t);
        var application = Application.Create(ValidJobSeekerId, null, null, null, clock).Value;

        application.LogFollowUp("Kontakt", clock);

        application.LastFollowUpAt.ShouldBe(application.FollowUps[0].CreatedAt);
        application.LastFollowUpAt.ShouldBe(t);
    }

    [Fact]
    public void LogFollowUp_RaisesFollowUpAddedDomainEvent()
    {
        var application = CreateActiveApplication();
        application.ClearDomainEvents();

        application.LogFollowUp("Kontakt", Clock);

        var evt = application.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<FollowUpAddedDomainEvent>();
        evt.ApplicationId.ShouldBe(application.Id);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void LogFollowUp_ProducesOutcomeThatIsNotPending_SoItCanNeverBeOverdue()
    {
        // The overdue-follow-up read predicate keys on Outcome == Pending &&
        // ScheduledAt < now. A logged follow-up is Outcome.Logged (never Pending),
        // so it can never satisfy that predicate — even though its ScheduledAt (=
        // now) is in the past from the very next instant. Small guard for the value-4
        // SmartEnum choice.
        var application = CreateActiveApplication();

        application.LogFollowUp("Kontakt", Clock);

        var followUp = application.FollowUps[0];
        followUp.Outcome.ShouldNotBe(FollowUpOutcome.Pending);
        followUp.Outcome.ShouldBe(FollowUpOutcome.Logged);
    }

    // ---------------------------------------------------------------
    // LogFollowUp — note-length validation (FollowUp.CreateLogged)
    // ---------------------------------------------------------------

    [Fact]
    public void LogFollowUp_WithNoteAtMaxLength_ReturnsSuccess()
    {
        var application = CreateActiveApplication();
        var maxNote = new string('A', 2000);

        var result = application.LogFollowUp(maxNote, Clock);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void LogFollowUp_WithNoteTooLong_ReturnsFailureAndAddsNothing()
    {
        var application = CreateActiveApplication();
        var tooLong = new string('A', 2001);

        var result = application.LogFollowUp(tooLong, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FollowUp.NoteTooLong");
        application.FollowUps.ShouldBeEmpty();
        application.LastFollowUpAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // LogFollowUp — blocked on a closed application (terminals + Ghosted)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Ghosted")]
    public void LogFollowUp_WhenClosedForActivity_ReturnsFailure_AddsNothing_DoesNotBumpLastFollowUpAt(
        string statusName)
    {
        var status = ApplicationStatus.FromName(statusName);
        var application = ClosedApplication(status);

        var result = application.LogFollowUp("Kontakt", Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.FollowUpNotAllowed");
        application.FollowUps.ShouldBeEmpty();
        application.LastFollowUpAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // LastFollowUpAt — shared reset scalar (AddFollowUp parity + monotonic)
    // ---------------------------------------------------------------

    [Fact]
    public void LastFollowUpAt_OnFreshApplication_IsNull()
    {
        var application = CreateActiveApplication();

        application.LastFollowUpAt.ShouldBeNull();
    }

    [Fact]
    public void AddFollowUp_BumpsLastFollowUpAtToFollowUpCreatedAt()
    {
        // Parity: a scheduled follow-up resets the wait exactly like a logged one.
        var t = new DateTimeOffset(2026, 5, 8, 9, 30, 0, TimeSpan.Zero);
        var clock = FakeDateTimeProvider.At(t);
        var application = Application.Create(ValidJobSeekerId, null, null, null, clock).Value;

        application.AddFollowUp(FollowUpChannel.Email, t.AddDays(5), null, clock);

        application.LastFollowUpAt.ShouldBe(application.FollowUps[0].CreatedAt);
        application.LastFollowUpAt.ShouldBe(t);
    }

    [Fact]
    public void SecondFollowUp_MovesLastFollowUpAtForward_Monotonic()
    {
        var t1 = new DateTimeOffset(2026, 5, 8, 9, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddDays(3);
        var application =
            Application.Create(ValidJobSeekerId, null, null, null, FakeDateTimeProvider.At(t1)).Value;

        application.AddFollowUp(FollowUpChannel.Email, t1.AddDays(5), null, FakeDateTimeProvider.At(t1));
        application.LastFollowUpAt.ShouldBe(t1);

        application.LogFollowUp("Uppföljning", FakeDateTimeProvider.At(t2));

        application.LastFollowUpAt.ShouldBe(t2);
        application.LastFollowUpAt!.Value.ShouldBeGreaterThan(t1);
    }
}
