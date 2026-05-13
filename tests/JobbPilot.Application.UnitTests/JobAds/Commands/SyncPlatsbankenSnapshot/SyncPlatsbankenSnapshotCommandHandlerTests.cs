using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.SyncPlatsbankenSnapshot;

/// <summary>
/// P8c refaktor: <see cref="SyncPlatsbankenSnapshotCommandHandler"/> är nu en tunn
/// shim runt <see cref="SyncPlatsbankenSnapshotJob"/>. Substantiella tester
/// (per-item upsert-loop, error-counts, race-skydd) ligger på job-nivån.
/// Här verifieras endast shim-proxy + resultat-mapping.
/// </summary>
public class SyncPlatsbankenSnapshotCommandHandlerTests
{
    private static JobAdImportItem ValidItem(string externalId = "ext-1") => new(
        ExternalId: externalId,
        Title: "Backend Developer",
        CompanyName: "Klarna",
        Description: "Job desc",
        Url: "https://jobs.example/1",
        PublishedAt: FakeDateTimeProvider.Default.UtcNow.AddDays(-1),
        ExpiresAt: FakeDateTimeProvider.Default.UtcNow.AddDays(30),
        SanitizedRawPayload: "{\"id\":\"ext-1\"}");

    [Fact]
    public async Task Handle_WithEmptySnapshot_ReturnsZeroCounts()
    {
        var handler = CreateHandlerWithJob(items: []);

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FetchedCount.ShouldBe(0);
        result.Value.AddedCount.ShouldBe(0);
        result.Value.UpdatedCount.ShouldBe(0);
        result.Value.SkippedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithItemsMappedToAdded_PropagatesCounts()
    {
        var handler = CreateHandlerWithJob(
            items: [ValidItem("ext-1"), ValidItem("ext-2")],
            upsertResult: Result.Success(UpsertOutcome.Added));

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FetchedCount.ShouldBe(2);
        result.Value.AddedCount.ShouldBe(2);
        result.Value.UpdatedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithItemsMappedToUpdated_PropagatesCounts()
    {
        var handler = CreateHandlerWithJob(
            items: [ValidItem("ext-1")],
            upsertResult: Result.Success(UpsertOutcome.Updated));

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), TestContext.Current.CancellationToken);

        result.Value.UpdatedCount.ShouldBe(1);
        result.Value.AddedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithSkippedItems_PropagatesAsSkippedCount()
    {
        var handler = CreateHandlerWithJob(
            items: [ValidItem("ext-1")],
            upsertResult: Result.Success(UpsertOutcome.Skipped));

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), TestContext.Current.CancellationToken);

        result.Value.AddedCount.ShouldBe(0);
        result.Value.SkippedCount.ShouldBe(1);
    }

    private static SyncPlatsbankenSnapshotCommandHandler CreateHandlerWithJob(
        IReadOnlyList<JobAdImportItem> items,
        Result<UpsertOutcome>? upsertResult = null)
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot(items, FakeDateTimeProvider.Default.UtcNow));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpsertExternalJobAdCommand>(), Arg.Any<CancellationToken>())
            .Returns(upsertResult ?? Result.Success(UpsertOutcome.Added));

        var job = new SyncPlatsbankenSnapshotJob(
            jobSource, mediator, FakeDateTimeProvider.Default,
            NullLogger<SyncPlatsbankenSnapshotJob>.Instance);

        return new SyncPlatsbankenSnapshotCommandHandler(job);
    }
}
