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
//      ResumeVersionConfiguration.Configure) flödar genom HasMany().WithOne()-
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

    private static readonly ResumeContent TailoredContent = new(
        new PersonalInfo("Klas Olsson", "klas@example.com", null, "Stockholm"),
        experiences:
        [
            new Experience("Mastercard", "Backend Developer", new DateOnly(2022, 1, 1), null, null),
        ],
        skills: [new Skill("C#", 8)],
        summary: "Skräddarsytt CV för en specifik annons.");

    /// <summary>
    /// Seedar ett CV vars TAILORED-version är soft-raderad genom produktionsvägen:
    /// <see cref="Resume.CreateTailored"/> skapar den, <see cref="Resume.DeleteVersion"/>
    /// raderar den. Ingen SQL, ingen kolumn-seed — aktören som skriver
    /// <c>deleted_at</c> är domänmetoden själv, och eftersom den är anropbar här
    /// asserterar testet att dess eget predikat släpper igenom tillståndet
    /// (CLAUDE.md §5 <c>Tests:</c>).
    ///
    /// Versionen är avsiktligt Tailored och inte Master: <c>DeleteVersion</c> vägrar
    /// Master (<c>Resume.MasterCannotBeDeleted</c>), så en soft-raderad Master är ett
    /// invariant-förbjudet tillstånd. Query-filtret som testas är detsamma för båda —
    /// det sitter på <c>ResumeVersion.DeletedAt</c> (se
    /// <c>ResumeVersionConfiguration.Configure</c>) och känner inte till <c>Kind</c> —
    /// så den relationella egenskapen bevisas lika starkt av det nåbara fallet.
    /// </summary>
    private static async Task<ResumeVersionId> SeedResumeWithSoftDeletedTailoredAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock,
        JobSeekerId seekerId, CancellationToken ct)
    {
        await EncryptionKeyTestSeed.WarmAsync(scope, seekerId, ct);
        var resume = Resume.Create(seekerId, "Mitt CV", "Klas Olsson", clock).Value;

        var tailored = resume.CreateTailored(TailoredContent, clock);
        tailored.IsSuccess.ShouldBeTrue(
            "ValidateContent måste acceptera TailoredContent — annars är premissen " +
            "för det här testet inte nåbar via domänen.");

        // `false` är inte ett antagande: ansökan skapas FÖRST efter den här
        // hjälparen, så ingen öppen ansökan kan referera versionen ännu. Det är
        // samma värde produktionens DeleteResumeVersionCommandHandler skulle
        // härleda för det här tillståndet.
        var deleted = resume.DeleteVersion(
            tailored.Value, isReferencedByOpenApplication: false, clock);
        deleted.IsSuccess.ShouldBeTrue(
            "Resume.DeleteVersion måste släppa igenom en Tailored-version som ingen " +
            "öppen ansökan refererar — annars är premissen för det här testet inte nåbar.");

        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        // Premissen måste nå DATABASEN, inte bara aggregatets minne. Utan den här
        // assertionen skulle en framtida hard-delete i DeleteVersion (`_versions
        // .Remove`) göra handlerns `AnyAsync` falsk därför att RADEN SAKNAS — testet
        // stannar grönt medan query-filtret aldrig utövas, och filhuvudets påstående
        // om SelectMany-filtret blir tyst falskt. `IgnoreQueryFilters` är enda sättet
        // att se raden som filtret är till för att dölja.
        db.ChangeTracker.Clear();
        var persisted = await db.Resumes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SelectMany(r => r.Versions)
            .SingleAsync(v => v.Id == tailored.Value, ct);
        persisted.DeletedAt.ShouldNotBeNull(
            "Raden måste finnas kvar med deleted_at satt — annars testar det som " +
            "följer frånvaro av en rad, inte query-filtret.");

        return tailored.Value;
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
        // Soft-raderad genom produktionsvägen (CreateTailored → DeleteVersion),
        // inte genom en UPDATE mot resume_versions. Se hjälparens doc-kommentar.
        var versionV = await SeedResumeWithSoftDeletedTailoredAsync(scope, db, clock, seekerA, ct);
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

        // Global query filter (DeletedAt == null) måste exkludera V genom
        // SelectMany → ownership-uppslaget missar → NotFound.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ResumeVersion.NotFound");

        // Handlern gör TVÅ uppslag: ownership och en existens-probe som avgör om
        // ett miss ska loggas som cross-user. Att lägga IgnoreQueryFilters på ENBART
        // proben (en rimlig "täpp till audit-blindfläcken"-ändring) skulle emittera
        // en cross-user-post för användarens EGET soft-raderade CV — en falsk post i
        // säkerhetsloggen (ADR 0031) som annars förblir osynlig för alla tre testerna.
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());

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
