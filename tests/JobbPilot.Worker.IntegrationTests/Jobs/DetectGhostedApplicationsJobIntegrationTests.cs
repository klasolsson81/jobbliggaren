using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace JobbPilot.Worker.IntegrationTests.Jobs;

/// <summary>
/// End-to-end smoke-test för <see cref="DetectGhostedApplicationsJob"/> mot riktig
/// Postgres (Testcontainers). Verifierar:
/// <list type="bullet">
/// <item>EF-translation av <c>StaleApplicationSpecification</c> mot Npgsql 10</item>
/// <item>Mediator-pipeline (5 behaviors) inklusive AuditBehavior</item>
/// <item>Atomisk persistens via UnitOfWork — Status flippar + audit-rad i samma transaction</item>
/// <item>Worker-stubs av audit-portarna ger <c>user_id = NULL</c> per ADR 0022</item>
/// </list>
///
/// Märkt <c>[Trait("Category", "SmokeTest")]</c> — körs INTE i default <c>dotnet test</c>.
/// Kör explicit: <c>dotnet test --filter "Category=SmokeTest"</c>.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class DetectGhostedApplicationsJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private async Task<DomainApplication> SeedApplicationAsync(
        ApplicationStatus targetStatus,
        DateTimeOffset statusChangeAt,
        bool softDeleted = false,
        CancellationToken ct = default)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seedClock = new FixedClock(statusChangeAt);
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Smoke User", seedClock).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, null, seedClock).Value;

        if (targetStatus == ApplicationStatus.Submitted ||
            targetStatus == ApplicationStatus.Acknowledged ||
            targetStatus == ApplicationStatus.Accepted)
        {
            app.TransitionTo(ApplicationStatus.Submitted, seedClock);
        }
        if (targetStatus == ApplicationStatus.Acknowledged)
        {
            app.TransitionTo(ApplicationStatus.Acknowledged, seedClock);
        }
        else if (targetStatus == ApplicationStatus.Accepted)
        {
            app.TransitionTo(ApplicationStatus.Acknowledged, seedClock);
            app.TransitionTo(ApplicationStatus.InterviewScheduled, seedClock);
            app.TransitionTo(ApplicationStatus.Interviewing, seedClock);
            app.TransitionTo(ApplicationStatus.OfferReceived, seedClock);
            app.TransitionTo(ApplicationStatus.Accepted, seedClock);
        }

        if (softDeleted)
            app.SoftDelete(seedClock);

        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app;
    }

    private async Task RunJobAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        // Ersätt IDateTimeProvider med fixed clock för deterministisk "now"
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IAppDbContext>();
        var mediator = sp.GetRequiredService<Mediator.IMediator>();
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<DetectGhostedApplicationsJob>();

        var job = new DetectGhostedApplicationsJob(db, mediator, new FixedClock(now), logger);
        await job.RunAsync(ct);
    }

    private async Task<DomainApplication?> ReadApplicationAsync(ApplicationId id, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    [Fact]
    public async Task RunAsync_StaleSubmittedApplication_TransitionsToGhostedAndWritesAuditEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var statusChangeAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero); // 37 dagar > 21
        var app = await SeedApplicationAsync(ApplicationStatus.Submitted, statusChangeAt, ct: ct);

        await RunJobAsync(now, ct);

        var updated = await ReadApplicationAsync(app.Id, ct);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Ghosted);

        // Verifiera audit-rad: Worker-stub ger user_id = NULL
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entries = await db.AuditLogEntries
            .Where(e => e.AggregateId == app.Id.Value && e.EventType == "Application.MarkedGhosted")
            .ToListAsync(ct);

        entries.Count.ShouldBe(1);
        var entry = entries[0];
        entry.AggregateType.ShouldBe("Application");
        entry.UserId.ShouldBeNull("Worker-system-jobb ska skriva audit-rad med user_id = NULL");
    }

    [Fact]
    public async Task RunAsync_StaleAcknowledgedApplication_TransitionsToGhosted()
    {
        var ct = TestContext.Current.CancellationToken;
        var statusChangeAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var app = await SeedApplicationAsync(ApplicationStatus.Acknowledged, statusChangeAt, ct: ct);

        await RunJobAsync(now, ct);

        var updated = await ReadApplicationAsync(app.Id, ct);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public async Task RunAsync_DraftApplication_RemainsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var statusChangeAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var app = await SeedApplicationAsync(ApplicationStatus.Draft, statusChangeAt, ct: ct);

        await RunJobAsync(now, ct);

        var updated = await ReadApplicationAsync(app.Id, ct);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Draft);
    }

    [Fact]
    public async Task RunAsync_RecentSubmittedApplication_RemainsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        // 5 dagar bakåt — väl inom 21-dagars default-threshold
        var statusChangeAt = new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var app = await SeedApplicationAsync(ApplicationStatus.Submitted, statusChangeAt, ct: ct);

        await RunJobAsync(now, ct);

        var updated = await ReadApplicationAsync(app.Id, ct);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Submitted);
    }

    [Fact]
    public async Task RunAsync_SoftDeletedApplication_RemainsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var statusChangeAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var app = await SeedApplicationAsync(
            ApplicationStatus.Submitted, statusChangeAt, softDeleted: true, ct: ct);

        await RunJobAsync(now, ct);

        // Global query filter: vi måste ignorera filter för att läsa soft-deleted
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updated = await db.Applications.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == app.Id, ct);

        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Submitted, "soft-deleted apps får inte ghostas");
    }

    [Fact]
    public async Task RunAsync_TerminalApplication_RemainsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var statusChangeAt = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var app = await SeedApplicationAsync(ApplicationStatus.Accepted, statusChangeAt, ct: ct);

        await RunJobAsync(now, ct);

        var updated = await ReadApplicationAsync(app.Id, ct);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe(ApplicationStatus.Accepted, "terminal-states ska aldrig ghostas");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
