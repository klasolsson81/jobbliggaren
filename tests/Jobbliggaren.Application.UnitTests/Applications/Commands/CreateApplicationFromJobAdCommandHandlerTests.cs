using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// #315 / ADR 0086 (D4 final ruling: concept-id-at-read). Write-handlern rör
// INTE taxonomi-ACL:en — den fryser JobAd:ens råa MunicipalityConceptId (STORED
// shadow) som-den-är; namn-resolvering sker på läs-vägen
// (GetApplicationByIdQueryHandler). Ctorn är därför tillbaka till 3 argument
// (db, currentUser, clock) — ingen ITaxonomyReadModel. Det codifierade
// read-side-only-invariantet för porten (TaxonomyAclLayerTests) hålls intakt.
public class CreateApplicationFromJobAdCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public CreateApplicationFromJobAdCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private async Task<(JobSeeker seeker, JobAd jobAd)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);

        var jobAd = JobAd.Create(
            "Backendutvecklare",
            Company.Create("Acme AB").Value,
            "Beskrivning",
            "https://example.com",
            JobSource.Manual,
            _clock.UtcNow,
            _clock.UtcNow.AddDays(30),
            _clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return (seeker, jobAd);
    }

    [Fact]
    public async Task Handle_WithValidJobAd_CreatesSubmittedApplicationLinkedToJobAd()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var handler = new CreateApplicationFromJobAdCommandHandler(db, _currentUser, _clock);

        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var app = await db.Applications.FirstOrDefaultAsync(ct);
        app.ShouldNotBeNull();
        app.JobAdId.ShouldBe(jobAd.Id);
        app.ManualPosting.ShouldBeNull();
        app.CoverLetter.ShouldBeNull();
        // "Har ansökt" → Submitted (not Draft) and stamps AppliedAt (#316/#332).
        app.Status.ShouldBe(Jobbliggaren.Domain.Applications.ApplicationStatus.Submitted);
        app.AppliedAt.ShouldBe(_clock.UtcNow);

        // #315 (ADR 0086): an AdSnapshot is captured from the seeded JobAd's fields
        // at apply-time. MunicipalityConceptId is null here — InMemory does not
        // compute the STORED municipality shadow column (the raw concept-id capture
        // is exercised against Postgres in Api.IntegrationTests). NO taxonomy
        // interaction on the write side (D4 final ruling).
        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Title.ShouldBe("Backendutvecklare");
        app.AdSnapshot.Company.ShouldBe("Acme AB");
        app.AdSnapshot.Source.ShouldBe(JobSource.Manual.Value);
        app.AdSnapshot.Description.ShouldBe("Beskrivning");
        app.AdSnapshot.MunicipalityConceptId.ShouldBeNull();
        app.AdSnapshot.CapturedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenJobAdMissing_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        _ = await SeedAsync(db, ct);
        var handler = new CreateApplicationFromJobAdCommandHandler(db, _currentUser, _clock);

        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(Guid.NewGuid()), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.NotFound");
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new CreateApplicationFromJobAdCommandHandler(db, currentUser, _clock);

        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.Unauthorized");
    }

    [Fact]
    public async Task Handle_RaisesApplicationCreatedDomainEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var handler = new CreateApplicationFromJobAdCommandHandler(db, _currentUser, _clock);

        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        var app = db.ChangeTracker.Entries<Jobbliggaren.Domain.Applications.Application>()
            .Select(e => e.Entity)
            .First();
        app.DomainEvents.ShouldNotBeEmpty();
    }
}
