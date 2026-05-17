using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;
using JobbPilot.Application.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Auditing.Jobs.AuditLogRetention;

/// <summary>
/// Test-coverage-sidospår B3 — branch-täckning för
/// <see cref="AuditLogRetentionJob"/>. Happy-path (end-to-end mot Postgres)
/// täcks av Worker.IntegrationTests; här täcks de rena
/// orchestrator-grenarna isolerat med mockad
/// <see cref="IAuditPartitionMaintainer"/> (senior-cto-advisor Approach (a)
/// — testa grenar direkt på den befintliga thin ADR 0032-orchestratorn,
/// ingen extract-to-service). Verifierar:
/// <list type="bullet">
/// <item>dropped.Count == 0 → no-partitions-gren (ingen kast)</item>
/// <item>dropped.Count &gt; 0 → foreach + summary, cutoff = now - 90d</item>
/// <item>pre-cancelled token → OCE propagerar, DropPartitions ej anropad</item>
/// </list>
/// </summary>
public class AuditLogRetentionJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 3, 0, 0, TimeSpan.Zero);

    private static AuditLogRetentionJob CreateJob(IAuditPartitionMaintainer maintainer) =>
        new(
            maintainer,
            new FakeDateTimeProvider(Now),
            NullLogger<AuditLogRetentionJob>.Instance);

    [Fact]
    public async Task RunAsync_WhenNoPartitionsDropped_CompletesWithoutThrowing()
    {
        // Branch (a) — dropped.Count == 0 → LogNoPartitionsToDrop-grenen.
        var maintainer = Substitute.For<IAuditPartitionMaintainer>();
        maintainer.EnsureNextDayPartitionAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns("audit_log_20260514");
        maintainer.DropPartitionsOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        var job = CreateJob(maintainer);

        await Should.NotThrowAsync(
            () => job.RunAsync(TestContext.Current.CancellationToken));

        await maintainer.Received(1).EnsureNextDayPartitionAsync(
            Now, Arg.Any<CancellationToken>());
        await maintainer.Received(1).DropPartitionsOlderThanAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenPartitionsDropped_UsesCutoffNinetyDaysBeforeNow()
    {
        // Branch (b) — dropped.Count > 0 → foreach + summary-grenen.
        // Assert: cutoff som passeras till DropPartitionsOlderThanAsync är
        // exakt now.AddDays(-90) (BUILD.md §7.1 + ADR 0022 90-dagars retention).
        var maintainer = Substitute.For<IAuditPartitionMaintainer>();
        maintainer.EnsureNextDayPartitionAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns("audit_log_20260514");

        var dropped = new[] { "audit_log_20260201", "audit_log_20260202" };
        maintainer.DropPartitionsOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(dropped);

        var job = CreateJob(maintainer);

        await job.RunAsync(TestContext.Current.CancellationToken);

        var expectedCutoff = Now.AddDays(-90);
        await maintainer.Received(1).DropPartitionsOlderThanAsync(
            expectedCutoff, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenTokenAlreadyCancelled_ThrowsAndDoesNotDropPartitions()
    {
        // Branch (c) — ct redan cancelled innan ThrowIfCancellationRequested().
        // EnsureNextDayPartitionAsync hinner anropas, men
        // DropPartitionsOlderThanAsync får ALDRIG anropas (cancellation-guard
        // mellan steg 1 och 2 — partition-drop är destruktiv DDL).
        var maintainer = Substitute.For<IAuditPartitionMaintainer>();
        maintainer.EnsureNextDayPartitionAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns("audit_log_20260514");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var job = CreateJob(maintainer);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));

        await maintainer.DidNotReceive().DropPartitionsOlderThanAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
