using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
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
// Testcontainers per senior-cto-advisor rev2 (B). Handlern projicerar
// ApplicationDetailDto via LEFT JOIN job_ads + FollowUps/Notes-include —
// relationell query-translation, ej en ren unit. Scenarier + assertions
// (inkl. ADR 0031/TD-67 cross-user failed-access-logg) bevarade 1:1;
// testnamn bevarade för spårbar täckning (ADR 0044). IFailedAccessLogger
// + ICurrentUser via NSubstitute — identiskt med unit-sviten, bara mot
// Npgsql-DbContext (handler-/auth-logik oförändrad). User-scoping bevaras
// via unik seedad user per test.
[Collection("Api")]
public class GetApplicationByIdQueryHandlerIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetApplicationByIdQueryHandlerIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
        IServiceScope scope,
        AppDbContext db,
        IDateTimeProvider clock,
        Guid userId,
        string? coverLetter = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        // TD-13 C3: värm ägar-DEK FÖRE krypterade entiteter läggs till
        // (direkt-seed förbi Mediator → FieldEncryptionKeyPrefetchBehavior
        // kör ej; speglas av denna helper). WarmAsync gör egen SaveChanges
        // som flushar pending JobSeeker (ej krypterad) — ofarligt.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);

        var app = DomainApplication.Create(seeker.Id, null, coverLetter, null, clock).Value;
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_ReturnsApplicationDetailDto()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId, "Mitt personliga brev.");

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(app.Id.Value);
        result.Status.ShouldBe("Draft");
        result.CoverLetter.ShouldBe("Mitt personliga brev.");
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_PopulatesFollowUps()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId);
        app.AddFollowUp(
            FollowUpChannel.Email,
            clock.UtcNow.AddDays(7),
            "Följ upp",
            clock);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.FollowUps.Count.ShouldBe(1);
        result.FollowUps[0].Channel.ShouldBe("Email");
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_PopulatesNotes()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId);
        app.AddNote("Bra arbetsgivare.", clock);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Notes.Count.ShouldBe(1);
        result.Notes[0].Content.ShouldBe("Bra arbetsgivare.");
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ReturnsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_ReturnsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedAsync(scope, db, clock, otherUserId);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(otherApp.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_LogsFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: ownership-mismatch loggas via IFailedAccessLogger.
        // Båda users måste ha JobSeeker-rad — annars returnerar handler null
        // via "jobSeekerId == default"-tidig-return innan ownership-checken.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedAsync(scope, db, clock, otherUserId);

        var ownSeeker = JobSeeker.Register(_userId, "Current User", clock).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger);

        await handler.Handle(new GetApplicationByIdQuery(otherApp.Id.Value), CancellationToken.None);

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application",
            otherApp.Id.Value,
            _userId,
            "GetApplicationById");
    }

    [Fact]
    public async Task Handle_WhenApplicationIdUnknown_DoesNotLogFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: okänt id är INTE cross-user-attempt — ska inte logga.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger);

        await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetApplicationByIdQueryHandler(db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }
}
