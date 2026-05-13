using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Jobs.SyncPlatsbanken;

/// <summary>
/// F2-P8c — SyncPlatsbankenSnapshotJob hämtar hela snapshot och delegerar per item
/// till mediator. Per-item-fel räknas men avbryter inte batchen. Verifierar:
/// <list type="bullet">
/// <item>Empty snapshot → SyncCounts alla 0</item>
/// <item>Added/Updated/Skipped räknas i rätt bucket</item>
/// <item>Per-item exception isolerad → ErrorCount++, batchen fortsätter</item>
/// <item>OperationCanceledException propagerar</item>
/// </list>
/// </summary>
public class SyncPlatsbankenSnapshotJobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static JobAdImportItem ValidItem(string externalId = "ext-1") => new(
        ExternalId: externalId,
        Title: "Backend-utvecklare",
        CompanyName: "Klarna",
        Description: "Beskrivning",
        Url: "https://example.com/jobs/1",
        PublishedAt: Now.AddDays(-1),
        ExpiresAt: Now.AddDays(30),
        SanitizedRawPayload: "{\"id\":\"ext-1\"}");

    private static IJobSource StubJobSource(params JobAdImportItem[] items)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot(items, Now));
        return jobSource;
    }

    private static SyncPlatsbankenSnapshotJob CreateJob(
        IJobSource jobSource,
        IMediator mediator,
        ISystemEventAuditor? auditor = null) =>
        new(
            jobSource, mediator, new FakeDateTimeProvider(Now),
            auditor ?? Substitute.For<ISystemEventAuditor>(),
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);

    [Fact]
    public async Task RunAsync_WithEmptySnapshot_ReturnsZeroCounts()
    {
        var jobSource = StubJobSource();
        var mediator = Substitute.For<IMediator>();
        var job = CreateJob(jobSource, mediator);

        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(0);
        counts.Added.ShouldBe(0);
        counts.Updated.ShouldBe(0);
        counts.Skipped.ShouldBe(0);
        counts.Errors.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenAllItemsAdded_IncrementsAddedCount()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"), ValidItem("c"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var job = CreateJob(jobSource, mediator);
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(3);
        counts.Added.ShouldBe(3);
        counts.Updated.ShouldBe(0);
        counts.Skipped.ShouldBe(0);
        counts.Errors.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenItemsMappedToSkipped_IncrementsSkippedCount()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Skipped));

        var job = CreateJob(jobSource, mediator);
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(2);
        counts.Skipped.ShouldBe(2);
        counts.Added.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenPerItemHandlerThrows_IncrementsErrorCountAndContinues()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"), ValidItem("c"));
        var mediator = Substitute.For<IMediator>();
        var callCount = 0;
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 2)
                    throw new InvalidOperationException("simulerad item-fail");
                return Result.Success(UpsertOutcome.Added);
            });

        var job = CreateJob(jobSource, mediator);
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(3);
        counts.Added.ShouldBe(2);
        counts.Errors.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_WhenHandlerReturnsFailure_IncrementsErrorCountWithoutThrowing()
    {
        var jobSource = StubJobSource(ValidItem("a"));
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpsertOutcome>(
                DomainError.Validation("Test.Failure", "simulerad")));

        var job = CreateJob(jobSource, mediator);
        var counts = await job.RunAsync(TestContext.Current.CancellationToken);

        counts.Fetched.ShouldBe(1);
        counts.Errors.ShouldBe(1);
        counts.Added.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationRequestedDuringBatch_PropagatesOperationCanceledException()
    {
        var jobSource = StubJobSource(ValidItem("a"), ValidItem("b"));
        var mediator = Substitute.For<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = CreateJob(jobSource, mediator);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));
    }
}
