using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Commands.ArchiveExternalJobAd;

/// <summary>
/// F2-P8c — arkivera extern JobAd vid removal-event per ADR 0032 §6.
/// Verifierar idempotent-semantik: redan arkiverad ger AlreadyArchived,
/// saknad JobAd ger NotFound (tyst acceptans av event-tapp).
/// </summary>
public class ArchiveExternalJobAdCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static ArchiveExternalJobAdCommandHandler CreateHandler(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db) =>
        new(
            db,
            new FakeDateTimeProvider(Now),
            NullLogger<ArchiveExternalJobAdCommandHandler>.Instance);

    private static async Task<JobAd> SeedExternalJobAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        string externalId,
        bool archive = false,
        CancellationToken ct = default)
    {
        var company = Company.Create("Klarna").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, externalId).Value;
        var clock = new FakeDateTimeProvider(Now.AddDays(-2));
        var jobAd = JobAd.Import(
            "Titel", company, "Beskrivning", "https://example.com/jobs/seed",
            external, "{\"id\":\"seed\"}", TestFacets.FromPayload("{\"id\":\"seed\"}"),
            [],
            Now.AddDays(-1), Now.AddDays(30), clock).Value;

        if (archive)
        {
            jobAd.Archive(clock);
        }

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd;
    }

    [Fact]
    public async Task Handle_WithActiveExternalJobAd_ReturnsArchivedAndFlipsStatus()
    {
        var db = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        var jobAd = await SeedExternalJobAdAsync(db, "ext-active", ct: ct);
        var handler = CreateHandler(db);
        var command = new ArchiveExternalJobAdCommand(JobSource.Platsbanken, "ext-active");

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ArchiveOutcome.Archived);
        jobAd.Status.ShouldBe(JobAdStatus.Archived);
    }

    [Fact]
    public async Task Handle_WithAlreadyArchivedJobAd_ReturnsAlreadyArchivedOutcome()
    {
        var db = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        await SeedExternalJobAdAsync(db, "ext-archived", archive: true, ct: ct);
        var handler = CreateHandler(db);
        var command = new ArchiveExternalJobAdCommand(JobSource.Platsbanken, "ext-archived");

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ArchiveOutcome.AlreadyArchived);
    }

    [Fact]
    public async Task Handle_WhenJobAdMissing_ReturnsNotFoundOutcome()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var command = new ArchiveExternalJobAdCommand(JobSource.Platsbanken, "ext-missing");

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ArchiveOutcome.NotFound);
    }

    [Fact]
    public async Task Handle_WithNullCommand_ThrowsArgumentNullException()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.Handle(null!, TestContext.Current.CancellationToken).AsTask());
    }
}
