using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Applications;
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
// GetApplicationByIdQueryHandlerIntegrationTests.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// RÖD svit (TDD) mot Testcontainers Postgres (relationell provider) — F4-11.
//
// Varför Npgsql och inte InMemory: EF InMemory hedrar varken
// SmartEnum→string-translation (Status != {terminal}) eller en betydelsefull
// kolumn-round-trip för nullable resume_version_id. Provider-translation är en
// känd InMemory-fälla (MEMORY: ef_strongly_typed_vo_contains_translation) — de
// här assertionerna MÅSTE köra mot riktig Postgres.
//
// Täcker:
//   * EF-persistens: AttachResumeVersion persisterar resume_version_id, round-
//     trippar på reload; ansökan utan attach → resume_version_id = NULL.
//   * == versionId-predikatets translation (fokuserad persistens-assert).
//   * DTO-yta: GetApplicationById returnerar ResumeVersionId efter attach.
[Collection("Api")]
public class ApplicationResumeVersionPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static async Task<(JobSeekerId seekerId, ResumeVersionId versionId)> SeedSeekerAndResumeAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        // Resume.Content krypteras (ADR 0049) → värm ägar-DEK FÖRE Add
        // (direkt-seed förbi Mediator-prefetch), samma kontrakt som
        // GetApplicationByIdQueryHandlerIntegrationTests.SeedAsync.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        return (seeker.Id, resume.MasterVersion.Id);
    }

    // ---------------------------------------------------------------
    // EF-persistens + backward-compat
    // ---------------------------------------------------------------

    [Fact]
    public async Task AttachResumeVersion_PersistsAndRoundTripsResumeVersionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, _userId, ct);
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.AttachResumeVersion(versionId, clock).IsSuccess.ShouldBeTrue();
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        reloaded.ResumeVersionId.ShouldNotBeNull();
        reloaded.ResumeVersionId!.Value.ShouldBe(versionId);
    }

    [Fact]
    public async Task Application_CreatedWithoutAttach_HasNullResumeVersionId()
    {
        // Bakåtkompatibilitet: kolumnen är nullable; ansökan utan attach → NULL.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        reloaded.ResumeVersionId.ShouldBeNull();
    }

    [Fact]
    public async Task QueryByResumeVersionId_TranslatesEqualityPredicate()
    {
        // Fokuserad translation-assert för "a.ResumeVersionId == versionId" mot
        // riktig Postgres (strongly-typed VO-jämförelse — InMemory missar detta).
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, _userId, ct);
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.AttachResumeVersion(versionId, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var found = await db.Applications
            .AsNoTracking()
            .AnyAsync(a => a.ResumeVersionId == versionId, ct);

        found.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // DTO-yta — GetApplicationById returnerar ResumeVersionId efter attach
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplicationById_AfterAttach_ReturnsResumeVersionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        _currentUser.UserId.Returns(_userId);

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, _userId, ct);
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.AttachResumeVersion(versionId, clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), ct);

        result.ShouldNotBeNull();
        result!.ResumeVersionId.ShouldBe(versionId.Value);
    }

    [Fact]
    public async Task GetApplicationById_WhenNotAttached_ReturnsNullResumeVersionId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        _currentUser.UserId.Returns(_userId);

        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);
        var app = DomainApplication.Create(seeker.Id, null, "Brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());
        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), ct);

        result.ShouldNotBeNull();
        result!.ResumeVersionId.ShouldBeNull();
    }
}
