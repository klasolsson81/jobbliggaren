using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Alias matchar Application.UnitTests GlobalUsings.cs (Application-typen
// krockar med Jobbliggaren.Application-namespacet); integrationsprojektet har
// ingen global alias, så den deklareras per fil.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Flyttad från Jobbliggaren.Application.UnitTests (EF InMemory) till Npgsql/
// Testcontainers per senior-cto-advisor rev2 (B), docs/reviews/
// 2026-05-17-fas3-stopp3a-divergence-cto-2.md §3/§4. Handlern joinar
// db.JobAds via converter på en nullable-struct-FK; EF InMemory är ej
// relationell och kan inte översätta converter + LEFT JOIN — testerna ÄR
// per definition integrationstester (Fowler/Cohn). Scenarier + assertions
// bevarade 1:1; provider bytt InMemory → Npgsql. Testnamn bevarade för
// spårbar täckning (ADR 0044 — ingen coverage-sänkning).
//
// Mönster (scope/AppDbContext/IDateTimeProvider) kopierat verbatim från
// ManualPostingPersistenceTests.cs (redan grön mot Npgsql). User-scoping
// (ADR 0031) bevaras: varje test seedar unik user via slumpad Guid →
// query-resultatet är naturligt isolerat per JobSeeker även mot delad
// Testcontainers-Postgres ([Collection("Api")]). Handlern konstrueras
// direkt med NSubstitute ICurrentUser — identiskt med unit-sviten, bara
// mot Npgsql-DbContext (handler-logik oförändrad → auth-semantik 1:1).
[Collection("Api")]
public class GetApplicationsQueryHandlerIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetApplicationsQueryHandlerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, List<DomainApplication> apps)> SeedAsync(
        AppDbContext db,
        IDateTimeProvider clock,
        Guid userId,
        int draftCount = 1,
        int submittedCount = 0)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        var apps = new List<DomainApplication>();
        for (var i = 0; i < draftCount; i++)
        {
            var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
            apps.Add(app);
            db.Applications.Add(app);
        }

        for (var i = 0; i < submittedCount; i++)
        {
            var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
            app.TransitionTo(ApplicationStatus.Submitted, clock);
            apps.Add(app);
            db.Applications.Add(app);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, apps);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyPagedResult()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetApplicationsQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmptyPagedResult()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithNoStatusFilter_ReturnsAllApplications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedAsync(db, clock, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_ProjectsAppliedAt_SetForSubmitted_NullForDraft()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, apps) = await SeedAsync(db, clock, _userId, draftCount: 1, submittedCount: 1);
        var submitted = apps.Single(a => a.Status == ApplicationStatus.Submitted);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        // #336: AppliedAt is projected into the read DTO here too (same DTO, two
        // call-sites). Draft → null; Submitted → the first-submit stamp.
        var draftDto = result.Items.Single(a => a.Status == "Draft");
        draftDto.AppliedAt.ShouldBeNull();

        var submittedDto = result.Items.Single(a => a.Status == "Submitted");
        submittedDto.AppliedAt.ShouldNotBeNull();
        // Postgres timestamptz truncates .NET ticks to microseconds → tolerance.
        submittedDto.AppliedAt!.Value.ShouldBe(submitted.AppliedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_WithStatusFilter_ReturnsOnlyMatchingApplications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedAsync(db, clock, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(Status: "Draft"), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(a => a.Status == "Draft");
    }

    [Fact]
    public async Task Handle_WithSubmittedStatusFilter_ExcludesDraftApplications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedAsync(db, clock, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(Status: "Submitted"), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        result.Items.ShouldAllBe(a => a.Status == "Submitted");
    }

    [Fact]
    public async Task Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedAsync(db, clock, _userId, draftCount: 1);

        var otherUserId = Guid.NewGuid();
        await SeedAsync(db, clock, otherUserId, draftCount: 3);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_TotalCount_IsIndependentOfPageSize()
    {
        // Regression-skydd: TotalCount ska vara total efter filter, inte page-storlek.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedAsync(db, clock, _userId, draftCount: 5);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(
            new GetApplicationsQuery(Page: 1, PageSize: 2),
            CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(5);
        result.TotalPages.ShouldBe(3);
    }
}
