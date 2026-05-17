using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.Applications.Queries.GetPipeline;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Alias matchar Application.UnitTests GlobalUsings.cs (Application-typen
// krockar med JobbPilot.Application-namespacet); integrationsprojektet har
// ingen global alias, så den deklareras per fil.
using DomainApplication = JobbPilot.Domain.Applications.Application;

namespace JobbPilot.Api.IntegrationTests.Applications;

// Flyttad från JobbPilot.Application.UnitTests (EF InMemory) till Npgsql/
// Testcontainers per senior-cto-advisor rev2 (B). Pipeline-handlern joinar
// db.JobAds FÖRE materialisering (ADR 0048 EN LEFT JOIN) — relationell
// query-translation, ej en ren unit. Scenarier + assertions bevarade 1:1;
// testnamn bevarade för spårbar täckning (ADR 0044). Mönster kopierat från
// ManualPostingPersistenceTests.cs. User-scoping (ADR 0031) bevaras via
// unik seedad user per test.
[Collection("Api")]
public class GetPipelineQueryHandlerIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetPipelineQueryHandlerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db,
        IDateTimeProvider clock,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task Handle_WhenNoApplications_ReturnsEmptyList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        await SeedSeekerAsync(db, clock, _userId);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyList()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetPipelineQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithApplicationsOfDifferentStatuses_GroupsByStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var draft1 = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var draft2 = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var submitted = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        submitted.TransitionTo(ApplicationStatus.Submitted, clock);

        db.Applications.Add(draft1);
        db.Applications.Add(draft2);
        db.Applications.Add(submitted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        var draftGroup = result.First(g => g.Status == "Draft");
        draftGroup.Count.ShouldBe(2);
        draftGroup.Applications.Count.ShouldBe(2);

        var submittedGroup = result.First(g => g.Status == "Submitted");
        submittedGroup.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WithSingleApplication_ReturnsSingleGroup()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Status.ShouldBe("Draft");
        result[0].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(app);

        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, clock, otherUserId);
        for (var i = 0; i < 5; i++)
        {
            db.Applications.Add(
                DomainApplication.Create(otherSeeker.Id, null, null, null, clock).Value);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Sum(g => g.Count).ShouldBe(1);
    }
}
