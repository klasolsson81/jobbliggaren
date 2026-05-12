using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.SyncPlatsbankenSnapshot;

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
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot([], FakeDateTimeProvider.Default.UtcNow));

        var db = TestAppDbContextFactory.Create();
        var handler = new SyncPlatsbankenSnapshotCommandHandler(
            jobSource, db, FakeDateTimeProvider.Default);

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FetchedCount.ShouldBe(0);
        result.Value.AddedCount.ShouldBe(0);
        result.Value.UpdatedCount.ShouldBe(0);
        result.Value.SkippedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithNewItems_AddsThemAsImportedJobAds()
    {
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot(
                [ValidItem("ext-1"), ValidItem("ext-2")],
                FakeDateTimeProvider.Default.UtcNow));

        var db = TestAppDbContextFactory.Create();
        var handler = new SyncPlatsbankenSnapshotCommandHandler(
            jobSource, db, FakeDateTimeProvider.Default);

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FetchedCount.ShouldBe(2);
        result.Value.AddedCount.ShouldBe(2);
        result.Value.UpdatedCount.ShouldBe(0);

        var jobAds = await db.JobAds.ToListAsync(TestContext.Current.CancellationToken);
        jobAds.Count.ShouldBe(2);
        jobAds.ShouldAllBe(j => j.External!.Source == JobSource.Platsbanken);
    }

    [Fact]
    public async Task Handle_WithExistingExternalReference_UpdatesInsteadOfAdding()
    {
        var db = TestAppDbContextFactory.Create();
        var clock = FakeDateTimeProvider.Default;

        // Pre-seed: en JobAd som redan finns med ExternalId "ext-1"
        var existing = JobAd.Import(
            title: "Old title",
            company: Company.Create("Klarna").Value,
            description: "Old desc",
            url: "https://jobs.example/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: "{\"old\":true}",
            publishedAt: clock.UtcNow.AddDays(-30),
            expiresAt: clock.UtcNow.AddDays(10),
            clock: clock).Value;
        db.JobAds.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot([ValidItem("ext-1")], clock.UtcNow));

        var handler = new SyncPlatsbankenSnapshotCommandHandler(jobSource, db, clock);
        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AddedCount.ShouldBe(0);
        result.Value.UpdatedCount.ShouldBe(1);

        var updated = await db.JobAds.FirstAsync(
            j => j.External!.ExternalId == "ext-1",
            TestContext.Current.CancellationToken);
        updated.Title.ShouldBe("Backend Developer");
        updated.Description.ShouldBe("Job desc");
    }

    [Fact]
    public async Task Handle_WithInvalidItem_IncrementsSkippedCount()
    {
        // Item med tom company → Company.Create failar → skipped.
        var invalidItem = ValidItem() with { CompanyName = string.Empty };
        var jobSource = Substitute.For<IJobSource>();
        jobSource.Source.Returns(JobSource.Platsbanken);
        jobSource.FetchSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new JobAdSnapshot([invalidItem], FakeDateTimeProvider.Default.UtcNow));

        var db = TestAppDbContextFactory.Create();
        var handler = new SyncPlatsbankenSnapshotCommandHandler(
            jobSource, db, FakeDateTimeProvider.Default);

        var result = await handler.Handle(
            new SyncPlatsbankenSnapshotCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AddedCount.ShouldBe(0);
        result.Value.SkippedCount.ShouldBe(1);
    }
}
