using JobbPilot.Application.Applications.Commands.CreateApplicationFromJobAd;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

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
        JobbPilot.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
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
    public async Task Handle_WithValidJobAd_CreatesApplicationLinkedToJobAd()
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
        var app = db.ChangeTracker.Entries<JobbPilot.Domain.Applications.Application>()
            .Select(e => e.Entity)
            .First();
        app.DomainEvents.ShouldNotBeEmpty();
    }
}
