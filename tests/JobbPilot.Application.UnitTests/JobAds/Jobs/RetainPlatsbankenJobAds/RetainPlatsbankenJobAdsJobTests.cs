using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Jobs.RetainPlatsbankenJobAds;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Jobs.RetainPlatsbankenJobAds;

/// <summary>
/// ADR 0032-amendment 2026-05-23 — snapshot-miss-retention + post-archive
/// circuit-breaker (CTO H1 + security-auditor 2026-05-23). Verifierar att
/// jobbet (a) delegerar till <c>IJobAdSnapshotMissTracker</c> med konfigurerad
/// tröskel, (b) skriver en aggregerad audit-rad (GDPR Art. 30), (c) aborterar
/// före <c>ExecuteUpdate</c> vid ratio > <c>MaxArchivePctPerRun</c>, och (d)
/// släpper igenom vid boundary (ratio == max). Faktisk SQL-translation testas
/// i Worker.IntegrationTests.
/// </summary>
public class RetainPlatsbankenJobAdsJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 23, 3, 15, 0, TimeSpan.Zero);

    private static RetainPlatsbankenJobAdsJob CreateJob(
        IJobAdSnapshotMissTracker tracker,
        ISystemEventAuditor auditor,
        int threshold = 3,
        double maxArchivePct = 0.25) =>
        new(
            tracker,
            Options.Create(new JobSourceRetentionOptions
            {
                SnapshotMissThreshold = threshold,
                MaxArchivePctPerRun = maxArchivePct,
            }),
            new FakeDateTimeProvider(Now),
            auditor,
            NullLogger<RetainPlatsbankenJobAdsJob>.Instance);

    private static IJobAdSnapshotMissTracker StubTracker(
        int active = 0, int candidates = 0, int archived = 0)
    {
        var tracker = Substitute.For<IJobAdSnapshotMissTracker>();
        tracker.CountActiveJobAdsAsync(Arg.Any<JobSource>(), Arg.Any<CancellationToken>())
            .Returns(active);
        tracker.CountArchiveCandidatesAsync(
                Arg.Any<JobSource>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidates);
        tracker.ArchiveJobAdsWithMissCountAtLeastAsync(
                Arg.Any<JobSource>(), Arg.Any<int>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(archived);
        return tracker;
    }

    [Fact]
    public async Task RunAsync_DelegatesToTrackerWithConfiguredThreshold()
    {
        var tracker = StubTracker(active: 100, candidates: 7, archived: 7);  // 7% < 25%
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(tracker, auditor, threshold: 5);

        await job.RunAsync(TestContext.Current.CancellationToken);

        await tracker.Received(1).ArchiveJobAdsWithMissCountAtLeastAsync(
            JobSource.Platsbanken, 5, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsAuditWithSnapshotMissReasonAndArchivedCount()
    {
        var tracker = StubTracker(active: 100, candidates: 12, archived: 12);
        var auditor = Substitute.For<ISystemEventAuditor>();
        JobAdsRetentionCompleted? captured = null;
        await auditor.RecordAsync(
            Arg.Do<JobAdsRetentionCompleted>(e => captured = e),
            Arg.Any<CancellationToken>());

        var job = CreateJob(tracker, auditor, threshold: 3);
        await job.RunAsync(TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured.Source.ShouldBe(JobSource.Platsbanken.Value);
        captured.Reason.ShouldBe("snapshot-miss");
        captured.ArchivedCount.ShouldBe(12);
        captured.Threshold.ShouldBe(3);
        captured.ThresholdAborted.ShouldBeFalse();
        captured.AbortReason.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_WhenZeroRowsAffected_StillRecordsAudit()
    {
        // GDPR Art. 30 — accountability gäller även när 0 rader arkiverades.
        var tracker = StubTracker();  // active=0, candidates=0, archived=0
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(tracker, auditor);

        await job.RunAsync(TestContext.Current.CancellationToken);

        await auditor.Received(1).RecordAsync(
            Arg.Is<JobAdsRetentionCompleted>(e => e.ArchivedCount == 0 && e.Reason == "snapshot-miss"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AbortsBeforeExecuteUpdate_WhenRatioExceedsMaxPct()
    {
        // CTO-rond 2026-05-23 H1 + security-auditor — post-archive circuit-breaker.
        // Arrange: 100 active, 30 candidates → ratio 30 % > 25 % default → ABORT.
        var tracker = StubTracker(active: 100, candidates: 30);
        var auditor = Substitute.For<ISystemEventAuditor>();
        JobAdsRetentionCompleted? captured = null;
        await auditor.RecordAsync(
            Arg.Do<JobAdsRetentionCompleted>(e => captured = e),
            Arg.Any<CancellationToken>());
        var job = CreateJob(tracker, auditor, threshold: 3, maxArchivePct: 0.25);

        // DomainException kastas (fail-loud → Hangfire-retry → CloudWatch alarm).
        var ex = await Should.ThrowAsync<DomainException>(
            () => job.RunAsync(TestContext.Current.CancellationToken));
        ex.Code.ShouldBe("RetainPlatsbankenJobAds.MaxArchivePctExceeded");

        // ExecuteUpdate-vägen får ALDRIG köras vid abort.
        await tracker.DidNotReceive().ArchiveJobAdsWithMissCountAtLeastAsync(
            Arg.Any<JobSource>(), Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        // Audit-rad SKRIVS FÖRE throw så det finns granskningsbart spår även
        // efter Hangfire-retry-loop (CTO H1.C).
        captured.ShouldNotBeNull();
        captured.ThresholdAborted.ShouldBeTrue();
        captured.AbortReason.ShouldBe("max-archive-pct-exceeded");
        captured.ArchivedCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_AllowsExecution_WhenRatioAtOrBelowMaxPct()
    {
        // CTO-rond 2026-05-23 H1.E — boundary-test: ratio == max (exakt) ska
        // släppas igenom. Off-by-one på > vs >= är klassisk bug. Vid 25/100
        // = exakt 25 % vid maxPct=0.25 → INTE abort (operator > inte >=).
        var tracker = StubTracker(active: 100, candidates: 25, archived: 25);
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(tracker, auditor, threshold: 3, maxArchivePct: 0.25);

        // Får inte kasta.
        await job.RunAsync(TestContext.Current.CancellationToken);

        // ExecuteUpdate ska ha körts.
        await tracker.Received(1).ArchiveJobAdsWithMissCountAtLeastAsync(
            JobSource.Platsbanken, 3, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        // Audit utan abort.
        await auditor.Received(1).RecordAsync(
            Arg.Is<JobAdsRetentionCompleted>(e =>
                !e.ThresholdAborted
                && e.AbortReason == null
                && e.ArchivedCount == 25),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenZeroActive_DoesNotAbort_AvoidsDivByZero()
    {
        // Defense: om active = 0 (tom korpus, oväntat men möjligt vid
        // jungfru-deploy) ska ratio behandlas som 0, inte NaN/div-by-zero.
        // Skydd mot fel som skulle blockera retention helt på en empty dev-DB.
        var tracker = StubTracker(active: 0, candidates: 0);
        var auditor = Substitute.For<ISystemEventAuditor>();
        var job = CreateJob(tracker, auditor);

        await Should.NotThrowAsync(() => job.RunAsync(TestContext.Current.CancellationToken));

        await tracker.Received(1).ArchiveJobAdsWithMissCountAtLeastAsync(
            Arg.Any<JobSource>(), Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
