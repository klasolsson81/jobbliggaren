using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

public class ApplicationStatusTests
{
    // ---------------------------------------------------------------
    // Draft
    // ---------------------------------------------------------------

    [Fact]
    public void Draft_AllowsTransitionTo_Submitted()
    {
        ApplicationStatus.Draft.AllowedTransitions.ShouldContain(ApplicationStatus.Submitted);
    }

    [Fact]
    public void Draft_DoesNotAllow_DirectTransitionToAcknowledged()
    {
        ApplicationStatus.Draft.AllowedTransitions.ShouldNotContain(ApplicationStatus.Acknowledged);
    }

    // ---------------------------------------------------------------
    // Submitted
    // ---------------------------------------------------------------

    [Fact]
    public void Submitted_AllowsTransitionTo_Acknowledged()
    {
        ApplicationStatus.Submitted.AllowedTransitions.ShouldContain(ApplicationStatus.Acknowledged);
    }

    [Fact]
    public void Submitted_AllowsTransitionTo_Rejected()
    {
        ApplicationStatus.Submitted.AllowedTransitions.ShouldContain(ApplicationStatus.Rejected);
    }

    [Fact]
    public void Submitted_AllowsTransitionTo_Withdrawn()
    {
        ApplicationStatus.Submitted.AllowedTransitions.ShouldContain(ApplicationStatus.Withdrawn);
    }

    [Fact]
    public void Submitted_DoesNotAllow_TransitionToGhosted()
    {
        // Ghosted nås via MarkGhosted (automatisk), inte TransitionTo (manuell)
        ApplicationStatus.Submitted.AllowedTransitions.ShouldNotContain(ApplicationStatus.Ghosted);
    }

    // ---------------------------------------------------------------
    // Accepted — terminaltillstånd
    // ---------------------------------------------------------------

    [Fact]
    public void Accepted_HasNoAllowedTransitions()
    {
        ApplicationStatus.Accepted.AllowedTransitions.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Rejected — terminaltillstånd
    // ---------------------------------------------------------------

    [Fact]
    public void Rejected_HasNoAllowedTransitions()
    {
        ApplicationStatus.Rejected.AllowedTransitions.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Withdrawn — terminaltillstånd
    // ---------------------------------------------------------------

    [Fact]
    public void Withdrawn_HasNoAllowedTransitions()
    {
        ApplicationStatus.Withdrawn.AllowedTransitions.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Ghosted — kan reaktiveras manuellt
    // ---------------------------------------------------------------

    [Fact]
    public void Ghosted_AllowsTransitionTo_Submitted()
    {
        ApplicationStatus.Ghosted.AllowedTransitions.ShouldContain(ApplicationStatus.Submitted);
    }

    [Fact]
    public void Ghosted_DoesNotAllow_TransitionToAccepted()
    {
        ApplicationStatus.Ghosted.AllowedTransitions.ShouldNotContain(ApplicationStatus.Accepted);
    }

    // ---------------------------------------------------------------
    // OfferReceived
    // ---------------------------------------------------------------

    [Fact]
    public void OfferReceived_AllowsTransitionTo_Accepted()
    {
        ApplicationStatus.OfferReceived.AllowedTransitions.ShouldContain(ApplicationStatus.Accepted);
    }

    [Fact]
    public void OfferReceived_AllowsTransitionTo_Rejected()
    {
        ApplicationStatus.OfferReceived.AllowedTransitions.ShouldContain(ApplicationStatus.Rejected);
    }

    [Fact]
    public void OfferReceived_AllowsTransitionTo_Withdrawn()
    {
        ApplicationStatus.OfferReceived.AllowedTransitions.ShouldContain(ApplicationStatus.Withdrawn);
    }
}
