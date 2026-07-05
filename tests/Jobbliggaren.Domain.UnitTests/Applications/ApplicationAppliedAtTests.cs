using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// #316 — AppliedAt-invariant. AppliedAt ("Datum sökt") stämplas på FÖRSTA
// övergången in i Submitted och skrivs ALDRIG om: varken av senare transitions
// (Submitted→Acknowledged→Rejected) eller av en Ghosted→Submitted-reaktivering.
// Det skiljer den från LastStatusChangeAt, som varje transition skriver om.
// Draft-skapade ansökningar har AppliedAt == null tills första Submit.
public class ApplicationAppliedAtTests
{
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static readonly DateTimeOffset T1 = new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------
    // Create — AppliedAt är null i Draft
    // ---------------------------------------------------------------

    [Fact]
    public void Create_DraftApplication_HasNullAppliedAt()
    {
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));

        application.Status.ShouldBe(ApplicationStatus.Draft);
        application.AppliedAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // TransitionTo(Submitted) — stämplar AppliedAt
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Submitted_StampsAppliedAtToClockUtcNow()
    {
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));

        application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        application.AppliedAt.ShouldBe(T1);
    }

    // ---------------------------------------------------------------
    // Senare transitions skriver INTE om AppliedAt
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_LaterTransition_DoesNotChangeAppliedAt()
    {
        // Submitted (t1) → Acknowledged (t2) → Rejected (t3). AppliedAt ska
        // förbli t1 (första submit) även om LastStatusChangeAt avancerar.
        var t2 = T1.AddDays(2);
        var t3 = T1.AddDays(5);
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));
        application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        application.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.At(t2));
        application.TransitionTo(ApplicationStatus.Rejected, FakeDateTimeProvider.At(t3));

        application.AppliedAt.ShouldBe(T1);
        application.LastStatusChangeAt.ShouldBe(t3); // bevisar att klockan avancerat
    }

    // ---------------------------------------------------------------
    // Ghosted→Submitted reaktivering bevarar ursprunglig AppliedAt
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ReactivationFromGhosted_KeepsOriginalAppliedAt()
    {
        // Submitted (stämpla t1) → TransitionTo(Ghosted) → TransitionTo(Submitted) vid t2.
        // AppliedAt ska vara t1, inte t2 — AF-rapporten vill månaden man
        // URSPRUNGLIGEN sökte, inte månaden man återöppnade en ghosted tråd.
        var t2 = T1.AddDays(30);
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));
        application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));
        application.TransitionTo(ApplicationStatus.Ghosted, FakeDateTimeProvider.At(T1.AddDays(22)));

        application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(t2));

        application.Status.ShouldBe(ApplicationStatus.Submitted);
        application.AppliedAt.ShouldBe(T1);
        application.LastStatusChangeAt.ShouldBe(t2); // bevisar att reaktiveringen skedde vid t2
    }

    [Fact]
    public void TransitionTo_ToGhosted_DoesNotChangeAppliedAt()
    {
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));
        application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        application.TransitionTo(ApplicationStatus.Ghosted, FakeDateTimeProvider.At(T1.AddDays(22)));

        application.AppliedAt.ShouldBe(T1);
    }

    // ---------------------------------------------------------------
    // Endast Submit stämplar AppliedAt — övriga fria/misslyckade övergångar inte
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_ToNonSubmittedStatus_DoesNotStampAppliedAt()
    {
        // ADR 0092 D3: Draft → Accepted är nu en tillåten fri övergång (Success),
        // men AppliedAt stämplas ENDAST på väg in i Submitted → förblir null här.
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));

        var result = application.TransitionTo(ApplicationStatus.Accepted, FakeDateTimeProvider.At(T1));

        result.IsSuccess.ShouldBeTrue();
        application.Status.ShouldBe(ApplicationStatus.Accepted);
        application.AppliedAt.ShouldBeNull();
    }

    [Fact]
    public void TransitionTo_OnSoftDeletedApplication_DoesNotStampAppliedAt()
    {
        // Den enda kvarvarande Failure-vägen (soft-delete-guarden) stämplar inte
        // heller AppliedAt.
        var application = CreateValidApplication(FakeDateTimeProvider.At(T1));
        application.SoftDelete(FakeDateTimeProvider.At(T1));

        var result = application.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.At(T1));

        result.IsFailure.ShouldBeTrue();
        application.AppliedAt.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Hjälpmetoder
    // ---------------------------------------------------------------

    private static Application CreateValidApplication(FakeDateTimeProvider clock) =>
        Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, clock).Value;
}
