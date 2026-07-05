using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Applications.Events;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// Spec: ADR 0092 D4 — append-only StatusChange-timeline. Varje status transition
// (TransitionTo + MarkGhosted) registrerar EN StatusChange i SAMMA aggregat-mutation
// (en UnitOfWork), så timelinen aldrig kan divergera från Status. Ett transition är
// ETT ögonblick: now fångas EN gång, så
// UpdatedAt == LastStatusChangeAt == StatusChange.ChangedAt == event-timestampen.
// ADR 0092 D3: övergångar är FRIA (ingen state-machine-guard kvar). De två
// kvarvarande guard-vägarna registrerar INGET: en soft-deletad ansökan (Failure)
// och en self-transition (no-op-Success). MarkGhosted:s no-op-väg registrerar INGET.
// SoftDelete kaskaderar till varje StatusChange (paritet med FollowUps/Notes).
// Speglar ApplicationAppliedAtTests / ApplicationAdSnapshotRetentionTests-mönstret
// (dedikerad concern-fil).
public class ApplicationStatusChangeTimelineTests
{
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static readonly DateTimeOffset T0 = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);
    private static readonly DateTimeOffset T2 = T0.AddDays(2);
    private static readonly DateTimeOffset T3 = T0.AddDays(5);

    // ---------------------------------------------------------------
    // TransitionTo — registrerar EN StatusChange med konsistent timestamp
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ValidTransition_AppendsSingleStatusChange()
    {
        var app = CreateDraft(T0);

        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        var change = app.StatusChanges.ShouldHaveSingleItem();
        change.From.ShouldBe(ApplicationStatus.Draft);
        change.To.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public void TransitionTo_ValidTransition_RecordsChangedAtEqualToLastStatusChangeAt()
    {
        // En transition är ETT ögonblick: ChangedAt måste vara EXAKT samma now
        // som LastStatusChangeAt (single-now-capture, ADR 0092 D4).
        var app = CreateDraft(T0);

        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        var change = app.StatusChanges.ShouldHaveSingleItem();
        change.ChangedAt.ShouldBe(T1);
        change.ChangedAt.ShouldBe(app.LastStatusChangeAt);
    }

    // ---------------------------------------------------------------
    // Sekventiella transitions ackumuleras i kronologisk ordning
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_SeveralSequentialTransitions_AccumulatesInChronologicalOrder()
    {
        // Draft→Submitted (T1) → Submitted→Acknowledged (T2). Två StatusChanges,
        // From/To kedjar korrekt ([0].To == [1].From), ChangedAt kronologiskt.
        var app = CreateDraft(T0);

        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.At(T2));

        app.StatusChanges.Count.ShouldBe(2);

        app.StatusChanges[0].From.ShouldBe(ApplicationStatus.Draft);
        app.StatusChanges[0].To.ShouldBe(ApplicationStatus.Submitted);
        app.StatusChanges[0].ChangedAt.ShouldBe(T1);

        app.StatusChanges[1].From.ShouldBe(ApplicationStatus.Submitted);
        app.StatusChanges[1].To.ShouldBe(ApplicationStatus.Acknowledged);
        app.StatusChanges[1].ChangedAt.ShouldBe(T2);

        // Kedjan är obruten: föregående To == nästa From.
        app.StatusChanges[0].To.ShouldBe(app.StatusChanges[1].From);
    }

    // ---------------------------------------------------------------
    // Guard-vägar registrerar INGET (ADR 0092 D3): soft-delete + self-transition
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_OnSoftDeletedApplication_ReturnsFailureAndRecordsNoStatusChange()
    {
        // En soft-deletad ansökan är utanför sin livscykel → Failure OCH ingen
        // timeline-rad. Ersätter den gamla InvalidTransition-guarden som fria
        // övergångar (ADR 0092 D3) tog bort.
        var app = CreateDraft(T0);
        app.SoftDelete(FakeDateTimeProvider.At(T1));

        var result = app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.DeletedCannotTransition");
        app.StatusChanges.ShouldBeEmpty();
    }

    [Fact]
    public void TransitionTo_SelfTransition_IsNoOpAndRecordsNoStatusChange()
    {
        // target == Status → no-op-Success: ingen From==To-rad registreras.
        var app = CreateDraft(T0);

        var result = app.TransitionTo(ApplicationStatus.Draft, FakeDateTimeProvider.At(T1));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Draft);
        app.StatusChanges.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // MarkGhosted registrerar previous→Ghosted
    // ---------------------------------------------------------------

    [Fact]
    public void MarkGhosted_FromSubmitted_RecordsSubmittedToGhostedStatusChange()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        app.MarkGhosted(FakeDateTimeProvider.At(T2));

        app.StatusChanges.Count.ShouldBe(2);
        var ghostChange = app.StatusChanges[1];
        ghostChange.From.ShouldBe(ApplicationStatus.Submitted);
        ghostChange.To.ShouldBe(ApplicationStatus.Ghosted);
        ghostChange.ChangedAt.ShouldBe(T2);
        ghostChange.ChangedAt.ShouldBe(app.LastStatusChangeAt);
    }

    // ---------------------------------------------------------------
    // MarkGhosted no-op registrerar INGET och ändrar inte status
    // ---------------------------------------------------------------

    [Fact]
    public void MarkGhosted_FromDraft_NoOp_RecordsNoStatusChangeAndKeepsStatus()
    {
        // Draft är varken Submitted eller Acknowledged → idempotent no-op.
        var app = CreateDraft(T0);

        var result = app.MarkGhosted(FakeDateTimeProvider.At(T1));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Draft);
        app.StatusChanges.ShouldBeEmpty();
    }

    [Fact]
    public void MarkGhosted_FromTerminal_NoOp_RecordsNoStatusChangeAndKeepsStatus()
    {
        // Submitted→Rejected (terminal, 2 changes). MarkGhosted från Rejected är
        // no-op → ingen NY change, status kvar Rejected.
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.Rejected, FakeDateTimeProvider.At(T2));
        var countBefore = app.StatusChanges.Count;

        var result = app.MarkGhosted(FakeDateTimeProvider.At(T3));

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Rejected);
        app.StatusChanges.Count.ShouldBe(countBefore);
    }

    // ---------------------------------------------------------------
    // SoftDelete kaskaderar till varje registrerad StatusChange
    // ---------------------------------------------------------------

    [Fact]
    public void SoftDelete_CascadesToEveryRecordedStatusChange()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.At(T2));

        app.SoftDelete(FakeDateTimeProvider.At(T3));

        app.StatusChanges.Count.ShouldBe(2);
        app.StatusChanges.ShouldAllBe(s => s.DeletedAt.HasValue);
    }

    // ---------------------------------------------------------------
    // Regressionsvakt (#7) — single-now-capture-refaktorn får inte ha brutit
    // AppliedAt-idempotensen. På FÖRSTA Submit delar AppliedAt, LastStatusChangeAt,
    // StatusChange.ChangedAt och event-timestampen exakt samma now; en senare
    // transition registrerar en ny change men om-stämplar INTE AppliedAt.
    // (Kompletterar ApplicationAppliedAtTests — duplicerar inte, binder ihop
    // invarianterna mot den nya timelinen.)
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_FirstSubmit_SharesOneTimestampAcrossAppliedAtLastChangeTimelineAndEvent()
    {
        var app = CreateDraft(T0);
        app.ClearDomainEvents();

        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T2));

        app.AppliedAt.ShouldBe(T2);
        app.LastStatusChangeAt.ShouldBe(T2);
        var change = app.StatusChanges.ShouldHaveSingleItem();
        change.ChangedAt.ShouldBe(T2);
        var evt = app.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ApplicationStatusTransitionedDomainEvent>();
        evt.OccurredAt.ShouldBe(T2);
    }

    [Fact]
    public void TransitionTo_LaterTransition_AppendsChangeButDoesNotRestampAppliedAt()
    {
        var app = CreateDraft(T0);
        app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T2));

        app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.At(T3));

        // AppliedAt stämplades EN gång (T2) och skrivs inte om av senare transition.
        app.AppliedAt.ShouldBe(T2);
        // Men timelinen växer och den nya changen delar T3 med LastStatusChangeAt.
        app.StatusChanges.Count.ShouldBe(2);
        app.StatusChanges[1].ChangedAt.ShouldBe(T3);
        app.StatusChanges[1].ChangedAt.ShouldBe(app.LastStatusChangeAt);
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateDraft(DateTimeOffset at) =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, FakeDateTimeProvider.At(at)).Value;
}
