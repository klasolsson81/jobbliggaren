using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// ADR 0092 D3: transitions are FREE. ApplicationStatus.RecommendedNextStatuses
// (renamed from the former AllowedTransitions) is now purely ADVISORY — a UI hint
// for the "Flytta till {nästa steg}" default + the avsluta/parkera options. It is
// NO LONGER a guard: Application.TransitionTo permits ANY of the ten statuses as a
// target regardless of what this set contains. These tests therefore assert only
// the graph CONTENTS (which conventional edges the hint exposes); enforcement is
// deliberately NOT asserted here (and free-transition behaviour is proven in
// ApplicationTests / ApplicationFreeTransitionTests). This class doubles as the
// rename-guard: if RecommendedNextStatuses regresses to AllowedTransitions, it fails
// to compile.
public class ApplicationStatusTests
{
    // ---------------------------------------------------------------
    // Draft
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_Draft_RecommendsSubmitted()
    {
        ApplicationStatus.Draft.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Submitted);
    }

    [Fact]
    public void RecommendedNextStatuses_Draft_DoesNotRecommendAcknowledged()
    {
        // Advisory only: Draft → Acknowledged is NOT a conventional next step, but
        // it is still a LEGAL free transition (this set no longer guards it).
        ApplicationStatus.Draft.RecommendedNextStatuses.ShouldNotContain(ApplicationStatus.Acknowledged);
    }

    // ---------------------------------------------------------------
    // Submitted
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_Submitted_RecommendsAcknowledged()
    {
        ApplicationStatus.Submitted.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Acknowledged);
    }

    [Fact]
    public void RecommendedNextStatuses_Submitted_RecommendsRejected()
    {
        ApplicationStatus.Submitted.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Rejected);
    }

    [Fact]
    public void RecommendedNextStatuses_Submitted_RecommendsWithdrawn()
    {
        ApplicationStatus.Submitted.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Withdrawn);
    }

    [Fact]
    public void RecommendedNextStatuses_Submitted_DoesNotRecommendGhosted()
    {
        // Ghosted is not a conventional onward step from Submitted, so it is not
        // recommended here. Manual Ghosted is nonetheless a LEGAL free transition
        // via TransitionTo (ADR 0092 D3) — the absence here is advisory, not a guard.
        ApplicationStatus.Submitted.RecommendedNextStatuses.ShouldNotContain(ApplicationStatus.Ghosted);
    }

    // ---------------------------------------------------------------
    // Acknowledged / InterviewScheduled / Interviewing — pipeline hints
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_Acknowledged_RecommendsInterviewScheduled()
    {
        ApplicationStatus.Acknowledged.RecommendedNextStatuses.ShouldContain(ApplicationStatus.InterviewScheduled);
    }

    [Fact]
    public void RecommendedNextStatuses_InterviewScheduled_RecommendsInterviewing()
    {
        ApplicationStatus.InterviewScheduled.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Interviewing);
    }

    [Fact]
    public void RecommendedNextStatuses_Interviewing_RecommendsOfferReceived()
    {
        ApplicationStatus.Interviewing.RecommendedNextStatuses.ShouldContain(ApplicationStatus.OfferReceived);
    }

    // ---------------------------------------------------------------
    // OfferReceived
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_OfferReceived_RecommendsAccepted()
    {
        ApplicationStatus.OfferReceived.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Accepted);
    }

    [Fact]
    public void RecommendedNextStatuses_OfferReceived_RecommendsRejected()
    {
        ApplicationStatus.OfferReceived.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Rejected);
    }

    [Fact]
    public void RecommendedNextStatuses_OfferReceived_RecommendsWithdrawn()
    {
        ApplicationStatus.OfferReceived.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Withdrawn);
    }

    // ---------------------------------------------------------------
    // Terminals — no conventional onward step (but reopening is a legal free move)
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_Accepted_IsEmpty()
    {
        // Empty = no conventional onward step surfaced in the UI. Reopening a
        // terminal (e.g. Accepted → Submitted) is still a LEGAL free transition.
        ApplicationStatus.Accepted.RecommendedNextStatuses.ShouldBeEmpty();
    }

    [Fact]
    public void RecommendedNextStatuses_Rejected_IsEmpty()
    {
        ApplicationStatus.Rejected.RecommendedNextStatuses.ShouldBeEmpty();
    }

    [Fact]
    public void RecommendedNextStatuses_Withdrawn_IsEmpty()
    {
        ApplicationStatus.Withdrawn.RecommendedNextStatuses.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Ghosted — reactivatable; conventional hint is back to Submitted
    // ---------------------------------------------------------------

    [Fact]
    public void RecommendedNextStatuses_Ghosted_RecommendsSubmitted()
    {
        ApplicationStatus.Ghosted.RecommendedNextStatuses.ShouldContain(ApplicationStatus.Submitted);
    }

    [Fact]
    public void RecommendedNextStatuses_Ghosted_DoesNotRecommendAccepted()
    {
        // Advisory only: Ghosted → Accepted is not a conventional hint, yet it is a
        // LEGAL free transition (this set no longer guards it).
        ApplicationStatus.Ghosted.RecommendedNextStatuses.ShouldNotContain(ApplicationStatus.Accepted);
    }
}
