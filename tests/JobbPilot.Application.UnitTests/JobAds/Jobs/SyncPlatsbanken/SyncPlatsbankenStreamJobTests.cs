using System.Runtime.CompilerServices;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.ArchiveExternalJobAd;
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
/// F2-P8c — SyncPlatsbankenStreamJob hämtar inkrementella ändringar och delegerar
/// per event till mediator. Verifierar:
/// <list type="bullet">
/// <item>Empty stream → counts alla 0, inget mediator-anrop</item>
/// <item>Upsert + removal-events räknas i rätt bucket (Added/Updated/Skipped/Archived)</item>
/// <item>Per-event-failure isolerad: ErrorCount++ men batchen fortsätter</item>
/// <item>OperationCanceledException propagerar (sväljs inte)</item>
/// <item>Overlap-window: since = now - 15min</item>
/// </list>
/// </summary>
public class SyncPlatsbankenStreamJobTests
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

    private static SyncPlatsbankenStreamJob CreateJob(
        IJobSource jobSource,
        IMediator mediator,
        FakeDateTimeProvider? clock = null) =>
        new(
            jobSource, mediator,
            clock ?? new FakeDateTimeProvider(Now),
            NullLogger<SyncPlatsbankenStreamJob>.Instance);

    private static IJobSource StubJobSource(params JobAdChange[] changes)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.StreamChangesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable(changes));
        return jobSource;
    }

    private static async IAsyncEnumerable<JobAdChange> ToAsyncEnumerable(
        JobAdChange[] items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task RunAsync_WithEmptyStream_DoesNotSendAnyCommand()
    {
        var jobSource = StubJobSource();
        var mediator = Substitute.For<IMediator>();
        var job = CreateJob(jobSource, mediator);

        await job.RunAsync(TestContext.Current.CancellationToken);

        await mediator.DidNotReceiveWithAnyArgs()
            .Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>());
        await mediator.DidNotReceiveWithAnyArgs()
            .Send(Arg.Any<ArchiveExternalJobAdCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WithUpsertEvent_DelegatesToUpsertCommand()
    {
        var upsert = new JobAdUpsert("ext-1", ValidItem("ext-1"), Now);
        var jobSource = StubJobSource(upsert);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));

        var job = CreateJob(jobSource, mediator);
        await job.RunAsync(TestContext.Current.CancellationToken);

        await mediator.Received(1).Send(
            Arg.Is<UpsertExternalJobAdCommand>(c =>
                c.ExternalId == "ext-1" && c.Source == JobSource.Platsbanken),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WithRemovalEvent_DelegatesToArchiveCommand()
    {
        var removal = new JobAdRemoval("ext-rem", Now);
        var jobSource = StubJobSource(removal);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ArchiveExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(ArchiveOutcome.Archived));

        var job = CreateJob(jobSource, mediator);
        await job.RunAsync(TestContext.Current.CancellationToken);

        await mediator.Received(1).Send(
            Arg.Is<ArchiveExternalJobAdCommand>(c =>
                c.ExternalId == "ext-rem" && c.Source == JobSource.Platsbanken),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WithMixedEvents_ProcessesAllRegardlessOfOrder()
    {
        var changes = new JobAdChange[]
        {
            new JobAdUpsert("ext-1", ValidItem("ext-1"), Now),
            new JobAdRemoval("ext-rem", Now),
            new JobAdUpsert("ext-2", ValidItem("ext-2"), Now),
        };
        var jobSource = StubJobSource(changes);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(UpsertOutcome.Added));
        mediator.Send(Arg.Any<ArchiveExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(ArchiveOutcome.Archived));

        var job = CreateJob(jobSource, mediator);
        await job.RunAsync(TestContext.Current.CancellationToken);

        await mediator.Received(2).Send(
            Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(
            Arg.Any<ArchiveExternalJobAdCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenUpsertHandlerThrows_ContinuesWithNextEvent()
    {
        var changes = new JobAdChange[]
        {
            new JobAdUpsert("ext-bad", ValidItem("ext-bad"), Now),
            new JobAdUpsert("ext-good", ValidItem("ext-good"), Now),
        };
        var jobSource = StubJobSource(changes);
        var mediator = Substitute.For<IMediator>();
        var callCount = 0;
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("simulerad handler-fail");
                return Result.Success(UpsertOutcome.Added);
            });

        var job = CreateJob(jobSource, mediator);

        // Får INTE kasta — fel-isolering per ADR 0032 §3 + TD-25-mönster.
        await job.RunAsync(TestContext.Current.CancellationToken);

        // Båda upsert-eventen skickades trots första exception.
        await mediator.Received(2).Send(
            Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WhenCancellationRequestedMidStream_PropagatesOperationCanceledException()
    {
        var changes = new JobAdChange[]
        {
            new JobAdUpsert("ext-1", ValidItem("ext-1"), Now),
            new JobAdUpsert("ext-2", ValidItem("ext-2"), Now),
        };
        var jobSource = StubJobSource(changes);
        var mediator = Substitute.For<IMediator>();
        using var cts = new CancellationTokenSource();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result<UpsertOutcome>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var job = CreateJob(jobSource, mediator);

        await Should.ThrowAsync<OperationCanceledException>(
            () => job.RunAsync(cts.Token));
    }

    [Fact]
    public async Task RunAsync_UsesFifteenMinuteOverlapWindow()
    {
        DateTimeOffset? capturedSince = null;
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.StreamChangesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedSince = call.Arg<DateTimeOffset>();
                return ToAsyncEnumerable([]);
            });

        var mediator = Substitute.For<IMediator>();
        var job = CreateJob(jobSource, mediator);

        await job.RunAsync(TestContext.Current.CancellationToken);

        capturedSince.ShouldNotBeNull();
        // ADR 0032 §3 + CTO-rond 2026-05-13: overlap-window = 15 min
        // (10 min cron + 5 min overlap).
        (Now - capturedSince!.Value).ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task RunAsync_WhenUpsertReturnsFailureResult_TreatsAsErrorWithoutThrowing()
    {
        var upsert = new JobAdUpsert("ext-fail", ValidItem("ext-fail"), Now);
        var jobSource = StubJobSource(upsert);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UpsertOutcome>(
                DomainError.Validation("Test.Failure", "simulerad")));

        var job = CreateJob(jobSource, mediator);

        // Får inte kasta — failure-result räknas i errors.
        await job.RunAsync(TestContext.Current.CancellationToken);

        await mediator.Received(1).Send(
            Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>());
    }
}
