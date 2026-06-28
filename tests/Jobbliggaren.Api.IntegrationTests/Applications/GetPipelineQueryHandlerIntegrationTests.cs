using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
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
    public async Task Handle_ProjectsAppliedAt_SetForSubmitted_NullForDraft()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        var draft = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        var submitted = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        submitted.TransitionTo(ApplicationStatus.Submitted, clock);

        db.Applications.Add(draft);
        db.Applications.Add(submitted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        // #336: AppliedAt is projected into the read DTO. Draft never submitted →
        // null; Submitted → the idempotent first-submit stamp. UpdatedAt is NOT a
        // valid proxy (it moves on every status change), hence the dedicated field.
        var draftDto = result.First(g => g.Status == "Draft").Applications.Single();
        draftDto.AppliedAt.ShouldBeNull();

        var submittedDto = result.First(g => g.Status == "Submitted").Applications.Single();
        submittedDto.AppliedAt.ShouldNotBeNull();
        // Postgres timestamptz truncates .NET ticks to microseconds, so compare
        // with a tolerance rather than exact equality on the round-tripped value.
        submittedDto.AppliedAt!.Value.ShouldBe(submitted.AppliedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ProjectsAppliedAt_SurvivesTransitionToTerminalStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);

        // Draft → Submitted (stamps AppliedAt) → Rejected (terminal). The domain
        // stamps AppliedAt once on first Submit and never overwrites it
        // (Application.cs), so it must survive into a terminal state. Klas Q1:
        // the row keeps "Skickad för X sedan" for terminal states — that copy is
        // anchored on AppliedAt, so the projection must still carry it here.
        var rejected = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        rejected.TransitionTo(ApplicationStatus.Submitted, clock);
        var stampedAt = rejected.AppliedAt;
        rejected.TransitionTo(ApplicationStatus.Rejected, clock);

        db.Applications.Add(rejected);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var rejectedDto = result.First(g => g.Status == "Rejected").Applications.Single();
        rejectedDto.AppliedAt.ShouldNotBeNull();
        // Same idempotent first-submit stamp — NOT re-stamped by the Rejected
        // transition. Postgres timestamptz → microsecond tolerance.
        rejectedDto.AppliedAt!.Value.ShouldBe(stampedAt!.Value, TimeSpan.FromSeconds(1));
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
