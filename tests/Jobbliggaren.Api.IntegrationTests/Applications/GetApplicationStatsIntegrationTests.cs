using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetApplicationStats;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Alias matches the Application.UnitTests GlobalUsings (the Application type
// collides with the Jobbliggaren.Application namespace); the integration project
// has no global alias so it is declared per file (pattern copied from
// GetPipelineQueryHandlerIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #313 — Testcontainers SMOKE for the stats handler. The metric math is covered
// exhaustively + DB-free in ApplicationStatsCalculatorTests, and the wiring on EF
// InMemory in GetApplicationStatsQueryHandlerTests. These prove what only a real
// Postgres can: the `Select(a => new { a.Status, a.AppliedAt })` projection +
// `.Take` valve translate, and the soft-delete global query filter (ADR 0048 c)
// applies server-side. User-scoping (ADR 0031) isolation via a unique seeded user
// per test — same pattern as GetPipelineQueryHandlerIntegrationTests.
[Collection("Api")]
public class GetApplicationStatsIntegrationTests
{
    private readonly ApiFactory _factory;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetApplicationStatsIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static DomainApplication SentWithStatus(
        JobSeekerId seekerId, IDateTimeProvider clock, params ApplicationStatus[] path)
    {
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        foreach (var step in path)
            app.TransitionTo(step, clock);
        return app;
    }

    [Fact]
    public async Task Handle_OverRealPostgres_ProjectsAndComputesMetrics()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        db.Applications.Add(DomainApplication.Create(seeker.Id, null, null, null, clock).Value); // Draft
        db.Applications.Add(SentWithStatus(seeker.Id, clock, ApplicationStatus.Submitted));
        db.Applications.Add(SentWithStatus(
            seeker.Id, clock, ApplicationStatus.Submitted, ApplicationStatus.Acknowledged));
        db.Applications.Add(SentWithStatus(
            seeker.Id, clock, ApplicationStatus.Submitted, ApplicationStatus.Rejected));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationStatsQueryHandler(db, _currentUser, clock);
        var result = await handler.Handle(new GetApplicationStatsQuery(), CancellationToken.None);

        result.TotalApplications.ShouldBe(4);
        result.TotalSent.ShouldBe(3); // draft excluded
        result.RejectionRate.Numerator.ShouldBe(1);
        result.RejectionRate.Denominator.ShouldBe(3);
        result.ResponseRate.Numerator.ShouldBe(1); // the Acknowledged one
        result.StatusCounts.Count.ShouldBe(10);
        result.MonthlyApplications.Count.ShouldBe(12);
        // Seeded with the real clock → all three sent in the current month, which is
        // the newest bucket inside the 12-month window.
        result.MonthlyApplications.Sum(m => m.Count).ShouldBe(3);
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeleted_ViaGlobalFilterOverRealPostgres()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var live = SentWithStatus(seeker.Id, clock, ApplicationStatus.Submitted);
        var deleted = SentWithStatus(seeker.Id, clock, ApplicationStatus.Submitted);
        deleted.SoftDelete(clock);
        db.Applications.Add(live);
        db.Applications.Add(deleted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationStatsQueryHandler(db, _currentUser, clock);
        var result = await handler.Handle(new GetApplicationStatsQuery(), CancellationToken.None);

        result.TotalApplications.ShouldBe(1);
        result.TotalSent.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_DoesNotLeakOtherJobSeekersApplications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        db.Applications.Add(SentWithStatus(seeker.Id, clock, ApplicationStatus.Submitted));

        var otherSeeker = await SeedSeekerAsync(db, clock, Guid.NewGuid());
        for (var i = 0; i < 4; i++)
            db.Applications.Add(SentWithStatus(otherSeeker.Id, clock, ApplicationStatus.Submitted));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationStatsQueryHandler(db, _currentUser, clock);
        var result = await handler.Handle(new GetApplicationStatsQuery(), CancellationToken.None);

        result.TotalApplications.ShouldBe(1);
    }
}
