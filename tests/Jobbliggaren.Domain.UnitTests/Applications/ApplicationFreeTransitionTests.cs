using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// Spec: ADR 0092 D3 — övergångar är FRIA. Application.TransitionTo tillåter vilken
// som helst av de tio statusarna som mål (framåt, bakåt, hopp, manuell Ghosted).
// Den gamla state-machine-guarden (Application.InvalidTransition) är borttagen och
// ersatt av undo-toast + full audit + StatusChange-timeline. Två hårda guards
// kvarstår: en soft-deletad ansökan kan inte transitiona, och en self-transition är
// en no-op. Övriga invarianter (single-now-capture, AppliedAt write-once,
// terminal AdSnapshot-minimering, StatusChange-registrering, transition-eventet)
// bevaras genom de fria vägarna. Dedikerad concern-fil (speglar
// ApplicationAppliedAtTests / ApplicationAdSnapshotRetentionTests-mönstret).
public class ApplicationFreeTransitionTests
{
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static readonly DateTimeOffset T0 = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);
    private static readonly DateTimeOffset T2 = T0.AddDays(1);
    private static readonly DateTimeOffset T3 = T0.AddDays(2);

    private const string MunicipalityConceptId = "1gEC_kvM_TXK";
    private const string DescriptionText = "En lång beskrivning av tjänsten.";

    // ---------------------------------------------------------------
    // Tidigare FÖRBJUDNA övergångar lyckas nu och registrerar en StatusChange
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Backward_SubmittedToDraft_SucceedsAndRecordsChange()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        var result = app.TransitionTo(ApplicationStatus.Draft, FakeDateTimeProvider.At(T2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Draft);
        var last = app.StatusChanges[^1];
        last.From.ShouldBe(ApplicationStatus.Submitted);
        last.To.ShouldBe(ApplicationStatus.Draft);
        last.ChangedAt.ShouldBe(T2);
    }

    [Fact]
    public void TransitionTo_Skip_DraftToOfferReceived_SucceedsAndRecordsChange()
    {
        var app = CreateDraft(T0);

        var result = app.TransitionTo(ApplicationStatus.OfferReceived, FakeDateTimeProvider.At(T1));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.OfferReceived);
        var change = app.StatusChanges.ShouldHaveSingleItem();
        change.From.ShouldBe(ApplicationStatus.Draft);
        change.To.ShouldBe(ApplicationStatus.OfferReceived);
    }

    [Fact]
    public void TransitionTo_InterviewScheduledToRejected_Succeeds()
    {
        // #566-luckan: att avslå direkt från InterviewScheduled var tidigare
        // otillåtet (InterviewScheduled hade bara Interviewing/Withdrawn). Nu fritt.
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.InterviewScheduled, FakeDateTimeProvider.At(T1));

        var result = app.TransitionTo(ApplicationStatus.Rejected, FakeDateTimeProvider.At(T2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Rejected);
        var last = app.StatusChanges[^1];
        last.From.ShouldBe(ApplicationStatus.InterviewScheduled);
        last.To.ShouldBe(ApplicationStatus.Rejected);
    }

    [Fact]
    public void TransitionTo_ReopenTerminal_AcceptedToSubmitted_SucceedsAndRecordsChange()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.Accepted, FakeDateTimeProvider.At(T1)); // hopp direkt till terminal

        var result = app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Submitted);
        var last = app.StatusChanges[^1];
        last.From.ShouldBe(ApplicationStatus.Accepted);
        last.To.ShouldBe(ApplicationStatus.Submitted);
    }

    // ---------------------------------------------------------------
    // Manuell Ghosted via TransitionTo — transition-event (INTE Ghosted-event),
    // registrerad StatusChange, och INGEN snapshot-minimering (Ghosted ej terminal)
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ManualGhostedFromSubmitted_RaisesTransitionedEventNotGhostedEvent()
    {
        var app = FromJobAdSubmitted();
        app.ClearDomainEvents();

        var result = app.TransitionTo(ApplicationStatus.Ghosted, FakeDateTimeProvider.At(T2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Ghosted);

        var evt = app.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationStatusTransitionedDomainEvent>();
        evt.Previous.ShouldBe(ApplicationStatus.Submitted);
        evt.Next.ShouldBe(ApplicationStatus.Ghosted);
        evt.OccurredAt.ShouldBe(T2);
    }

    [Fact]
    public void TransitionTo_ManualGhostedFromSubmitted_RecordsStatusChange()
    {
        var app = FromJobAdSubmitted();

        app.TransitionTo(ApplicationStatus.Ghosted, FakeDateTimeProvider.At(T2));

        var last = app.StatusChanges[^1];
        last.From.ShouldBe(ApplicationStatus.Submitted);
        last.To.ShouldBe(ApplicationStatus.Ghosted);
        last.ChangedAt.ShouldBe(T2);
    }

    [Fact]
    public void TransitionTo_ManualGhosted_DoesNotMinimiseAdSnapshot()
    {
        // Ghosted är reaktiverbart (inte terminal) → snapshot-beskrivningen behålls.
        var app = FromJobAdSubmitted();

        app.TransitionTo(ApplicationStatus.Ghosted, FakeDateTimeProvider.At(T2));

        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBe(DescriptionText);
    }

    // ---------------------------------------------------------------
    // Soft-delete-guard — en borttagen ansökan kan inte transitiona (till NÅGOT mål)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Draft")]
    [InlineData("Ghosted")]
    [InlineData("Accepted")]
    public void TransitionTo_OnSoftDeletedApplication_ReturnsFailureAndRecordsNothing(string target)
    {
        var app = CreateDraft(T0);
        app.SoftDelete(FakeDateTimeProvider.At(T1));

        var result = app.TransitionTo(ApplicationStatus.FromName(target), FakeDateTimeProvider.At(T2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.DeletedCannotTransition");
        app.Status.ShouldBe(ApplicationStatus.Draft);
        app.StatusChanges.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Self-transition — no-op: Success, ingen StatusChange, inget event,
    // ingen UpdatedAt/LastStatusChangeAt-förändring
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_SelfTransition_IsNoOpAndDoesNotMutateOrRaiseEvent()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        var updatedAtBefore = app.UpdatedAt;
        var lastChangeBefore = app.LastStatusChangeAt;
        var changeCountBefore = app.StatusChanges.Count;
        app.ClearDomainEvents();

        var result = app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T2));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Submitted);
        app.StatusChanges.Count.ShouldBe(changeCountBefore); // ingen From==To-rad
        app.DomainEvents.ShouldBeEmpty();                    // inget transition-event
        app.UpdatedAt.ShouldBe(updatedAtBefore);             // now fångas aldrig
        app.LastStatusChangeAt.ShouldBe(lastChangeBefore);
    }

    // ---------------------------------------------------------------
    // Regression — AppliedAt-idempotens + terminal AdSnapshot-minimering håller
    // genom en FRI väg (submit → bakåt till Draft → hopp till terminal Accepted)
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ThroughFreeBackwardPathToTerminal_KeepsAppliedAtAndMinimisesSnapshot()
    {
        var app = FromJobAdSubmitted();            // Submit @ T1 → AppliedAt = T1
        app.TransitionTo(ApplicationStatus.Draft, FakeDateTimeProvider.At(T2));      // bakåt
        app.TransitionTo(ApplicationStatus.Accepted, FakeDateTimeProvider.At(T3));   // hopp till terminal

        // AppliedAt stämplades EN gång (T1) och skrivs aldrig om av senare fria hopp.
        app.AppliedAt.ShouldBe(T1);
        app.Status.ShouldBe(ApplicationStatus.Accepted);
        // Terminal → beskrivningen minimeras, men metadatan (titel) behålls.
        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBeNull();
        app.AdSnapshot.Title.ShouldBe("Backend-utvecklare");
        // Varje fritt hopp registrerades: Submit, Submitted→Draft, Draft→Accepted.
        app.StatusChanges.Count.ShouldBe(3);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateDraft(DateTimeOffset at) =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, FakeDateTimeProvider.At(at)).Value;

    private static AdSnapshot SnapshotWithDescription() =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: MunicipalityConceptId,
            url: "https://example.com/jobb/1",
            source: "Platsbanken",
            publishedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            description: DescriptionText,
            capturedAt: T0, contacts: null);

    // JobAd-kopplad ansökan med beskriven snapshot, submittad @ T1 (AppliedAt = T1).
    private static Application FromJobAdSubmitted()
    {
        var app = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, SnapshotWithDescription(), null,
            FakeDateTimeProvider.At(T0)).Value;
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        return app;
    }
}
