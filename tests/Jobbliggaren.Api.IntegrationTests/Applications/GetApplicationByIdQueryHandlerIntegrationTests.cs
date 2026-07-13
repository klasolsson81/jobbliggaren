using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
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

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

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

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

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

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Notes.Count.ShouldBe(1);
        result.Notes[0].Content.ShouldBe("Bra arbetsgivare.");
    }

    // ---------------------------------------------------------------
    // ADR 0092 D4 — StatusChange-timelinen surfaceras oldest-first i detail-DTO:n,
    // och är TOM för en ansökan utan transitions (back-compat, ingen backfill).
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenApplicationHasTransitions_PopulatesStatusChangesOldestFirst()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId);

        // Fasta, distinkta klockor per transition → deterministisk oldest-first
        // ordning + exakta ChangedAt-assertions (0 sub-sekund → round-trippar
        // exakt genom Postgres timestamptz).
        var t1 = new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
        var t2 = t1.AddDays(2);
        app.TransitionTo(ApplicationStatus.Submitted, ClockAt(t1));
        app.TransitionTo(ApplicationStatus.Acknowledged, ClockAt(t2));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.StatusChanges.Count.ShouldBe(2);
        // Oldest-first (handlern OrderBy(ChangedAt)); FE vänder för newest-first.
        result.StatusChanges[0].From.ShouldBe("Draft");
        result.StatusChanges[0].To.ShouldBe("Submitted");
        result.StatusChanges[0].ChangedAt.ShouldBe(t1);
        result.StatusChanges[1].From.ShouldBe("Submitted");
        result.StatusChanges[1].To.ShouldBe("Acknowledged");
        result.StatusChanges[1].ChangedAt.ShouldBe(t2);
    }

    [Fact]
    public async Task Handle_WhenApplicationHasNoTransitions_ReturnsEmptyStatusChanges()
    {
        // Draft-ansökan utan transitions (t.ex. pre-timeline-rad) → tom lista,
        // ingen krasch. Bevisar att timelinen aldrig fabriceras/backfillas.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, app) = await SeedAsync(scope, db, clock, _userId);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.StatusChanges.ShouldBeEmpty();
    }

    // Fast klocka per transition (StatusChange.ChangedAt fångas från clock.UtcNow).
    // NSubstitute — samma mock-bibliotek som resten av sviten; ett värde per
    // instans gör transitions-ordningen deterministisk.
    private static IDateTimeProvider ClockAt(DateTimeOffset instant)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(instant);
        return clock;
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

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

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

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

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
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger, Substitute.For<ITaxonomyReadModel>());

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
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger, Substitute.For<ITaxonomyReadModel>());

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

        var handler = new GetApplicationByIdQueryHandler(db, currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // #315 (ADR 0086) — PreservedAd (AdSnapshotDto) surfaceras i detail-DTO:n
    // för en JobAd-länkad ansökan, men är null för en manuell/cover-letter-only.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenApplicationHasAdSnapshot_PopulatesPreservedAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        // En JobAd-länkad ansökan med snapshot skapas BARA via "Har ansökt"-vägen
        // (CreateApplicationFromJobAdCommandHandler) — den enda writer:n som sätter
        // ett AdSnapshot. Seeda via den handlern så snapshot:et fångas naturligt.
        var jobAd = JobAd.Create(
            "Backend-utvecklare", Company.Create("Klarna").Value, "En beskrivning.",
            "https://example.com/jobb/1", JobSource.Platsbanken,
            clock.UtcNow, clock.UtcNow.AddDays(30), clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        // Write-handlern (3-arg ctor, INGEN taxonomi — D4 final ruling) fryser
        // JobAd:ens råa MunicipalityConceptId; namn-resolvering sker på läs-vägen.
        var createHandler = new CreateApplicationFromJobAdCommandHandler(
            db, _currentUser, clock);
        var created = await createHandler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        // Read-handlern fick ITaxonomyReadModel (4:e param) — resolverar snapshot:ets
        // concept-id → namn i PreservedAd.Location.
        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), taxonomy);

        var result = await handler.Handle(
            new GetApplicationByIdQuery(created.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.PreservedAd.ShouldNotBeNull();
        result.PreservedAd!.Title.ShouldBe("Backend-utvecklare");
        result.PreservedAd.Company.ShouldBe("Klarna");
        result.PreservedAd.Description.ShouldBe("En beskrivning.");
        // JobAd.Create bär ingen kommun (raw_payload saknas) → STORED-shadow null →
        // captured concept-id null → resolverad Location null (graceful).
        result.PreservedAd.Location.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenApplicationIsManual_PreservedAdIsNull()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // Cover-letter-only ansökan (Application.Create) sätter aldrig ett snapshot.
        var (_, app) = await SeedAsync(scope, db, clock, _userId, "Bara brev");

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.PreservedAd.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // #315 (ADR 0086 D4) — read-time ort-resolvering: en JobAd-länkad ansökan
    // vars JobAd bär ett municipality-concept-id → PreservedAd.Location resolveras
    // till svensk label av read-handlern (riktig ITaxonomyReadModel). Importerad
    // JobAd m. raw_payload + facetter (#841: facetterna skrivs i C# vid ingest, EJ av Postgres).
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenAdSnapshotHasMunicipality_ResolvesPreservedAdLocation()
    {
        const string olofstromConceptId = "1gEC_kvM_TXK";
        const string olofstromLabel = "Olofström";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);

        // Importerad JobAd m. municipality_concept_id på den path STORED-kolumnen läser.
        var externalId = $"ext-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"workplace_address\":{{" +
            $"\"municipality_concept_id\":\"{olofstromConceptId}\"}}}}";
        var jobAd = JobAd.Import(
            "Backend-utvecklare", Company.Create("Klarna").Value, "En beskrivning.",
            "https://example.com/jobb/1",
            ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload, TestFacets.FromPayload(rawPayload), clock.UtcNow, null, clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(CancellationToken.None);

        var createHandler = new CreateApplicationFromJobAdCommandHandler(db, _currentUser, clock);
        var created = await createHandler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), taxonomy);

        var result = await handler.Handle(
            new GetApplicationByIdQuery(created.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.PreservedAd.ShouldNotBeNull();
        result.PreservedAd!.Location.ShouldBe(olofstromLabel);
    }
}
