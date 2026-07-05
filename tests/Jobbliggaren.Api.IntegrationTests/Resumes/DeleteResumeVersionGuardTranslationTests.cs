using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// RÖD svit (TDD) mot Testcontainers Postgres — F4-11 delete-guard.
//
// CTO-OBLIGATORISKT: delete-guard-predikatet i DeleteResumeVersionCommandHandler
// blir
//   db.Applications.AsNoTracking().AnyAsync(
//       a => a.ResumeVersionId == versionId
//         && a.Status != ApplicationStatus.Accepted
//         && a.Status != ApplicationStatus.Rejected
//         && a.Status != ApplicationStatus.Withdrawn, ct)
// "Non-terminal" = INTE i {Accepted, Rejected, Withdrawn}. Ghosted BLOCKERAR
// radering (reaktiverbar). Detta är den enda FÄRSKA translation-ytan i F4-11:
// SmartEnum→string "!=" mot riktig Postgres. EF InMemory hedrar inte den
// translationen → testet MÅSTE köra mot Npgsql.
//
// Dessa tre testar predikatet EXAKT som handlern kör det, mot riktigt seedade
// Applications i olika status. De är konstruktörbara utan en Tailored-factory
// och utgör den kanoniska translation-bevisningen.
//
// (Det kompletterande end-to-end-handler-testet — DeleteResumeVersion →
// Conflict "Resume.VersionInUse" — ligger i
// DeleteResumeVersionInUseHandlerTests och har en känd API-blocker: se rapport.)
[Collection("Api")]
public class DeleteResumeVersionGuardTranslationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static async Task<(JobSeekerId seekerId, ResumeVersionId versionId)> SeedSeekerAndResumeAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);
        return (seeker.Id, resume.MasterVersion.Id);
    }

    /// <summary>
    /// Bygger en Application som refererar <paramref name="versionId"/> och vars
    /// Status nått <paramref name="target"/> via TransitionTo-vägar,
    /// och persisterar den. Returnerar inget — testet frågar via predikatet.
    /// </summary>
    private static async Task SeedApplicationReferencingVersionAsync(
        AppDbContext db, IDateTimeProvider clock, JobSeekerId seekerId,
        ResumeVersionId versionId, ApplicationStatus target, CancellationToken ct)
    {
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.AttachResumeVersion(versionId, clock);

        if (target != ApplicationStatus.Draft)
        {
            app.TransitionTo(ApplicationStatus.Submitted, clock);

            if (target == ApplicationStatus.Ghosted)
            {
                app.TransitionTo(ApplicationStatus.Ghosted, clock);
            }
            else if (target == ApplicationStatus.Rejected)
            {
                app.TransitionTo(ApplicationStatus.Rejected, clock);
            }
            else if (target == ApplicationStatus.Withdrawn)
            {
                app.TransitionTo(ApplicationStatus.Withdrawn, clock);
            }
            else if (target == ApplicationStatus.Accepted)
            {
                app.TransitionTo(ApplicationStatus.Acknowledged, clock);
                app.TransitionTo(ApplicationStatus.InterviewScheduled, clock);
                app.TransitionTo(ApplicationStatus.Interviewing, clock);
                app.TransitionTo(ApplicationStatus.OfferReceived, clock);
                app.TransitionTo(ApplicationStatus.Accepted, clock);
            }
            else if (target != ApplicationStatus.Submitted)
            {
                throw new InvalidOperationException($"Ej hanterad status: {target}");
            }
        }

        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
    }

    // Predikatet exakt som handlern kör det.
    private static Task<bool> IsReferencedByOpenApplicationAsync(
        AppDbContext db, ResumeVersionId versionId, CancellationToken ct) =>
        db.Applications.AsNoTracking().AnyAsync(
            a => a.ResumeVersionId == versionId
              && a.Status != ApplicationStatus.Accepted
              && a.Status != ApplicationStatus.Rejected
              && a.Status != ApplicationStatus.Withdrawn, ct);

    // ---------------------------------------------------------------
    // Non-terminal referens (Submitted) → predikatet TRUE (skulle blockera)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Guard_WhenReferencingAppIsSubmitted_PredicateIsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, ct);
        await SeedApplicationReferencingVersionAsync(
            db, clock, seekerId, versionId, ApplicationStatus.Submitted, ct);
        db.ChangeTracker.Clear();

        var blocked = await IsReferencedByOpenApplicationAsync(db, versionId, ct);

        blocked.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Ghosted referens → predikatet TRUE (Ghosted blockerar radering)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Guard_WhenReferencingAppIsGhosted_PredicateIsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, ct);
        await SeedApplicationReferencingVersionAsync(
            db, clock, seekerId, versionId, ApplicationStatus.Ghosted, ct);
        db.ChangeTracker.Clear();

        var blocked = await IsReferencedByOpenApplicationAsync(db, versionId, ct);

        blocked.ShouldBeTrue("Ghosted är reaktiverbar och ska blockera radering");
    }

    // ---------------------------------------------------------------
    // Endast terminala referenser → predikatet FALSE (radering tillåten)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    public async Task Guard_WhenOnlyReferencingAppIsTerminal_PredicateIsFalse(string terminalStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (seekerId, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, ct);
        await SeedApplicationReferencingVersionAsync(
            db, clock, seekerId, versionId, ApplicationStatus.FromName(terminalStatus), ct);
        db.ChangeTracker.Clear();

        var blocked = await IsReferencedByOpenApplicationAsync(db, versionId, ct);

        blocked.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Ingen referens alls → predikatet FALSE
    // ---------------------------------------------------------------

    [Fact]
    public async Task Guard_WhenNoApplicationReferencesVersion_PredicateIsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var (_, versionId) = await SeedSeekerAndResumeAsync(scope, db, clock, ct);
        db.ChangeTracker.Clear();

        var blocked = await IsReferencedByOpenApplicationAsync(db, versionId, ct);

        blocked.ShouldBeFalse();
    }
}
