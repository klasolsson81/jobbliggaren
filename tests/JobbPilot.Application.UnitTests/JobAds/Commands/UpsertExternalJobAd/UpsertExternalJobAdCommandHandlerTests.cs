using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.UpsertExternalJobAd;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.UpsertExternalJobAd;

/// <summary>
/// F2-P8c — race-säker upsert per ADR 0032 §5. Verifierar:
/// <list type="bullet">
/// <item>Happy path: Add → SaveChanges → <see cref="UpsertOutcome.Added"/></item>
/// <item>Domain-validation failures → <see cref="UpsertOutcome.Skipped"/> (Company.Name tom, JobAd-invariants)</item>
/// <item>ExternalReference-validation failure (Source=Manual) → Skipped</item>
/// <item>UNIQUE-violation → detach + reload existing + UpdateFromSource → <see cref="UpsertOutcome.Updated"/></item>
/// <item>UNIQUE-violation men existing redan borttagen → Skipped</item>
/// <item>DbUpdateException som INTE är UNIQUE-violation → propagerar (handler sväljer inte okända DB-fel)</item>
/// </list>
/// </summary>
public class UpsertExternalJobAdCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static JobAdImportItem ValidItem(
        string externalId = "ext-1",
        string title = "Backend-utvecklare",
        string companyName = "Klarna",
        string description = "Vi söker en backend-utvecklare.",
        string url = "https://example.com/jobs/1") =>
        new(
            ExternalId: externalId,
            Title: title,
            CompanyName: companyName,
            Description: description,
            Url: url,
            PublishedAt: Now.AddDays(-1),
            ExpiresAt: Now.AddDays(30),
            SanitizedRawPayload: "{\"id\":\"ext-1\"}");

    private static UpsertExternalJobAdCommandHandler CreateHandler(
        IAppDbContext db,
        IDbExceptionInspector? inspector = null) =>
        new(
            db,
            inspector ?? Substitute.For<IDbExceptionInspector>(),
            new FakeDateTimeProvider(Now),
            NullLogger<UpsertExternalJobAdCommandHandler>.Instance);

    [Fact]
    public async Task Handle_WithNewExternalJobAd_AddsAndReturnsAddedOutcome()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem("ext-new");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-new", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Added);
        var persisted = await db.JobAds.AsNoTracking()
            .FirstOrDefaultAsync(j => j.External!.ExternalId == "ext-new",
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.External!.Source.ShouldBe(JobSource.Platsbanken);
    }

    [Fact]
    public async Task Handle_WithEmptyCompanyName_SkipsViaCompanyValidation()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem(companyName: "   ");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
        (await db.JobAds.AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithManualSource_SkipsViaExternalReferenceValidation()
    {
        // ExternalReference.Create avvisar JobSource.Manual.
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem();
        var command = new UpsertExternalJobAdCommand(JobSource.Manual, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WithEmptyTitle_SkipsViaJobAdImportValidation()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem(title: "   ");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WhenUniqueViolation_DetachesAndReloadsExistingForUpdate()
    {
        // Seed existing via riktig in-memory DbContext.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        await SeedExistingExternalJobAdAsync(seedDb, "ext-collision", "Old Title", ct);

        // Mock IAppDbContext för att tvinga DbUpdateException på första SaveChanges.
        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var saveCallCount = 0;
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                {
                    throw new DbUpdateException("UNIQUE-violation simulerad");
                }
                return Task.FromResult(1);
            });

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-collision", title: "New Title");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-collision", item);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Updated);
        db.Received(1).Detach(Arg.Any<JobAd>());
    }

    [Fact]
    public async Task Handle_WhenUniqueViolationButExistingMissing_ReturnsSkipped()
    {
        // Empty store — INSERT skall failas av oss, men "reload" hittar inget.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DbUpdateException("UNIQUE-violation"));

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-gone");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-gone", item);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WhenNonUniqueDbUpdateException_PropagatesException()
    {
        // Andra DB-fel (t.ex. constraint NOT NULL, deadlock) får inte sväljas.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var thrown = new DbUpdateException("annan DB-fail");
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw thrown);

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(false);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-other");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-other", item);

        var ex = await Should.ThrowAsync<DbUpdateException>(
            () => handler.Handle(command, ct).AsTask());
        ex.ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task Handle_WithNullCommand_ThrowsArgumentNullException()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.Handle(null!, TestContext.Current.CancellationToken).AsTask());
    }

    private static async Task SeedExistingExternalJobAdAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        string externalId,
        string title,
        CancellationToken ct)
    {
        var company = Company.Create("Klarna").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, externalId).Value;
        var clock = new FakeDateTimeProvider(Now.AddDays(-2));
        var jobAd = JobAd.Import(
            title, company, "Beskrivning", "https://example.com/jobs/seed",
            external, "{\"id\":\"seed\"}",
            Now.AddDays(-1), Now.AddDays(30), clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }
}
