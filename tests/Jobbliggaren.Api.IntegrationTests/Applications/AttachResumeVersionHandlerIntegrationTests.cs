using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Application-typen krockar med Jobbliggaren.Application-namespacet; per-fil-alias
// (integrationsprojektet har ingen global alias) — speglar
// ApplicationResumeVersionPersistenceTests.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Relationell (Testcontainers Postgres) fail-closed-regression för F4-11
// AttachResumeVersionCommandHandler. Härdar två IDOR-/soft-delete-egenskaper som
// idag enbart bevisas mot EF InMemory (unit-sviten):
//
//   1) Cross-user version → NotFound + audit, ansökan ej muterad.
//      Bevisar att den JobSeekerId-scopade `SelectMany(r => r.Versions)` är
//      fail-closed mot riktig Npgsql — fångar en framtida regression som byter
//      SelectMany mot en join eller lägger till IgnoreQueryFilters.
//
//   2) Soft-raderad EGEN version → NotFound (global query filter genom SelectMany).
//      Bevisar att ResumeVersion-filtret (DeletedAt == null,
//      ResumeVersionConfiguration.cs:67) flödar genom HasMany().WithOne()-
//      navigationen (riktig relation, ej owned) ut i handlerns ownership-uppslag.
//
// Varför Npgsql och inte InMemory: EF InMemory modellerar INTE global query filter
// genom en SelectMany-navigation troget (känd fälla, MEMORY:
// ef_strongly_typed_vo_contains_translation). De här assertionerna MÅSTE köra mot
// riktig Postgres för att vara meningsfulla.
//
// Dessa tester ska vara GRÖNA mot nuvarande produktionskod (handlern är redan
// implementerad och korrekt) — hardening/regression, inte RED.
[Collection("Api")]
public class AttachResumeVersionHandlerIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<JobSeekerId> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    private static async Task<ResumeVersionId> SeedResumeForAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock,
        JobSeekerId seekerId, CancellationToken ct)
    {
        // Resume.Content krypteras (ADR 0049) → värm ägar-DEK FÖRE Add
        // (direkt-seed förbi Mediator-prefetch), samma kontrakt som
        // ApplicationResumeVersionPersistenceTests.SeedSeekerAndResumeAsync.
        await EncryptionKeyTestSeed.WarmAsync(scope, seekerId, ct);
        var resume = Resume.Create(seekerId, "Mitt CV", "Klas Olsson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);
        return resume.MasterVersion.Id;
    }

    private static AttachResumeVersionCommandHandler CreateHandler(
        AppDbContext db, IDateTimeProvider clock,
        ICurrentUser currentUser, IFailedAccessLogger failedAccessLogger) =>
        new(db, currentUser, clock, failedAccessLogger);

    // ---------------------------------------------------------------
    // IDOR: version tillhör en ANNAN users Resume (relationell)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenVersionBelongsToAnotherUsersResume_ReturnsNotFoundAndLogsCrossUserAndDoesNotMutate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userIdA = Guid.NewGuid();
        var userIdB = Guid.NewGuid();

        // Användare A: egen JobSeeker + Application + egen Resume (så att A:s
        // version-uppslag är icke-tomt → starkare regression-vakt mot ett byte
        // av SelectMany). DEK värms för A via SeedResumeForAsync.
        var seekerA = await SeedSeekerAsync(db, clock, userIdA, ct);
        await SeedResumeForAsync(scope, db, clock, seekerA, ct);
        var appA = DomainApplication.Create(seekerA, null, null, null, clock).Value;
        db.Applications.Add(appA);
        await db.SaveChangesAsync(ct);

        // Användare B: egen JobSeeker + Resume → Master-version. DEK värms för B.
        var seekerB = await SeedSeekerAsync(db, clock, userIdB, ct);
        var versionB = await SeedResumeForAsync(scope, db, clock, seekerB, ct);

        db.ChangeTracker.Clear();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userIdA);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, clock, currentUser, failedAccessLogger);

        var command = new AttachResumeVersionCommand(appA.Id.Value, versionB.Value);
        var result = await handler.Handle(command, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");
        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "ResumeVersion", versionB.Value, userIdA, "AttachResumeVersion");

        // Ansökan får INTE muteras (resume_version_id måste förbli NULL i DB).
        db.ChangeTracker.Clear();
        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == appA.Id, ct);
        reloaded.ResumeVersionId.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Soft-raderad EGEN version → global filter genom SelectMany (relationell)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOwnVersionIsSoftDeleted_ReturnsNotFoundAndDoesNotMutate()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userIdA = Guid.NewGuid();
        var seekerA = await SeedSeekerAsync(db, clock, userIdA, ct);
        var versionV = await SeedResumeForAsync(scope, db, clock, seekerA, ct);
        var appA = DomainApplication.Create(seekerA, null, null, null, clock).Value;
        db.Applications.Add(appA);
        await db.SaveChangesAsync(ct);

        // Soft-radera Master-versionen V på DB-nivå. Detta är ETT MEDVETET test-
        // seam: domänen vägrar radera en Master (Resume.DeleteVersion →
        // "Resume.MasterCannotBeDeleted") och ingen Resume.CreateTailored-factory
        // finns ännu (deferred promotion-STEG), så DB-nivå-setup är enda sättet
        // att exercera soft-deleted-version-vägen relationellt idag. Använd
        // scope:ts IDateTimeProvider för tidsstämpeln (aldrig DateTime.Now).
        var deletedAt = clock.UtcNow;
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE resume_versions SET deleted_at = {0} WHERE id = {1}",
            [deletedAt, versionV.Value], ct);
        db.ChangeTracker.Clear();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userIdA);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, clock, currentUser, failedAccessLogger);

        var command = new AttachResumeVersionCommand(appA.Id.Value, versionV.Value);
        var result = await handler.Handle(command, ct);

        // Global query filter (DeletedAt == null) måste exkludera V genom
        // SelectMany → ownership-uppslaget missar → NotFound.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");

        db.ChangeTracker.Clear();
        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == appA.Id, ct);
        reloaded.ResumeVersionId.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Happy path (relationell) — positiv SelectMany-väg round-trippar i DB
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenUserOwnsAppAndVersion_ReturnsSuccessAndPersistsResumeVersionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var userIdA = Guid.NewGuid();
        var seekerA = await SeedSeekerAsync(db, clock, userIdA, ct);
        var versionV = await SeedResumeForAsync(scope, db, clock, seekerA, ct);
        var appA = DomainApplication.Create(seekerA, null, null, null, clock).Value;
        db.Applications.Add(appA);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userIdA);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = CreateHandler(db, clock, currentUser, failedAccessLogger);

        var command = new AttachResumeVersionCommand(appA.Id.Value, versionV.Value);
        var result = await handler.Handle(command, ct);

        // Handlern muterar det spårade aggregatet; persistera + round-trippa.
        result.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == appA.Id, ct);
        reloaded.ResumeVersionId.ShouldNotBeNull();
        reloaded.ResumeVersionId!.Value.ShouldBe(versionV);

        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }
}
