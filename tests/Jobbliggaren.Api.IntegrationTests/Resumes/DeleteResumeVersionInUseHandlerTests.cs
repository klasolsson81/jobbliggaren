using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// RÖD svit (TDD) — det kompletterande end-to-end-handler-testet som F4-11 sköt upp
// (se DeleteResumeVersionGuardTranslationTests rad 33–36). Tidigare blockerare:
// Resume saknade en publik factory för att skapa en Tailored-version, så en
// VersionInUse-Conflict kunde inte konstrueras via det riktiga handler-flödet.
// Fas 4 STEG A öppnar Resume.CreateTailored → testet är nu konstruerbart.
//
// Till skillnad från DeleteResumeVersionGuardTranslationTests (som testar
// guard-PREDIKATET isolerat) kör detta HELA DeleteResumeVersionCommandHandler
// mot riktig Postgres: guard-translation + Resume.DeleteVersion-aggregatet +
// Result-returen, end-to-end. Master-checken kortsluter inte VersionInUse-grenen
// eftersom målet är en Tailored-version.
[Collection("Api")]
public class DeleteResumeVersionInUseHandlerTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly ResumeContent TailoredContent = new(
        new PersonalInfo("Klas Olsson", "klas@example.com", null, "Stockholm"),
        experiences:
        [
            new Experience("Mastercard", "Backend Developer", new DateOnly(2022, 1, 1), null, null),
        ],
        skills: [new Skill("C#", 8)],
        summary: "Skräddarsytt CV för en specifik annons.");

    private sealed record SeededResume(
        Guid UserId,
        JobSeekerId SeekerId,
        ResumeId ResumeId,
        ResumeVersionId TailoredVersionId);

    /// <summary>
    /// Seedar en seeker (med känt UserId), ett CV med en Tailored-version och en
    /// Application i <paramref name="applicationStatus"/> som refererar Tailored-
    /// versionen. DEK värms FÖRE Application-add (krypterat cover_letter-fält).
    /// </summary>
    private static async Task<SeededResume> SeedAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock,
        ApplicationStatus applicationStatus, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, ct);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", clock).Value;
        var tailoredVersionId = resume.CreateTailored(TailoredContent, clock).Value;
        db.Resumes.Add(resume);

        var app = DomainApplication.Create(seeker.Id, null, null, null, clock).Value;
        app.AttachResumeVersion(tailoredVersionId, clock);
        TransitionToStatus(app, applicationStatus, clock);
        db.Applications.Add(app);

        await db.SaveChangesAsync(ct);

        return new SeededResume(userId, seeker.Id, resume.Id, tailoredVersionId);
    }

    private static void TransitionToStatus(
        DomainApplication app, ApplicationStatus target, IDateTimeProvider clock)
    {
        if (target == ApplicationStatus.Draft) return;

        app.TransitionTo(ApplicationStatus.Submitted, clock);
        if (target == ApplicationStatus.Submitted) return;

        if (target == ApplicationStatus.Withdrawn)
        {
            app.TransitionTo(ApplicationStatus.Withdrawn, clock);
            return;
        }

        if (target == ApplicationStatus.Rejected)
        {
            app.TransitionTo(ApplicationStatus.Rejected, clock);
            return;
        }

        if (target == ApplicationStatus.Accepted)
        {
            app.TransitionTo(ApplicationStatus.Acknowledged, clock);
            app.TransitionTo(ApplicationStatus.InterviewScheduled, clock);
            app.TransitionTo(ApplicationStatus.Interviewing, clock);
            app.TransitionTo(ApplicationStatus.OfferReceived, clock);
            app.TransitionTo(ApplicationStatus.Accepted, clock);
            return;
        }

        throw new InvalidOperationException($"Ej hanterad status: {target}");
    }

    private static DeleteResumeVersionCommandHandler BuildHandler(IServiceScope scope, Guid userId)
    {
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var failedAccessLogger = scope.ServiceProvider.GetRequiredService<IFailedAccessLogger>();
        return new DeleteResumeVersionCommandHandler(db, currentUser, clock, failedAccessLogger);
    }

    // ---------------------------------------------------------------
    // Non-terminal (Submitted) referens → Conflict "Resume.VersionInUse"
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenTailoredVersionReferencedByNonTerminalApplication_ReturnsVersionInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeded = await SeedAsync(scope, db, clock, ApplicationStatus.Submitted, ct);
        db.ChangeTracker.Clear();

        var handler = BuildHandler(scope, seeded.UserId);
        var command = new DeleteResumeVersionCommand(
            seeded.ResumeId.Value, seeded.TailoredVersionId.Value);

        var result = await handler.Handle(command, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.VersionInUse");
    }

    // ---------------------------------------------------------------
    // Endast terminal (Withdrawn) referens → radering tillåten (Success)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenTailoredVersionReferencedByTerminalApplication_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeded = await SeedAsync(scope, db, clock, ApplicationStatus.Withdrawn, ct);
        db.ChangeTracker.Clear();

        var handler = BuildHandler(scope, seeded.UserId);
        var command = new DeleteResumeVersionCommand(
            seeded.ResumeId.Value, seeded.TailoredVersionId.Value);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
    }
}
