using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedJobAds.Commands;

public class SaveJobAdCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDbExceptionInspector _inspector = Substitute.For<IDbExceptionInspector>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public SaveJobAdCommandHandlerTests()
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
    public async Task Handle_WithValidJobAd_SavesAndReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var handler = new SaveJobAdCommandHandler(db, _currentUser, _clock, _inspector);

        var result = await handler.Handle(new SaveJobAdCommand(jobAd.Id.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        var saved = await db.SavedJobAds.FirstOrDefaultAsync(ct);
        saved.ShouldNotBeNull();
        saved.JobAdId.ShouldBe(jobAd.Id);
    }

    [Fact]
    public async Task Handle_WhenAlreadySaved_ReturnsSuccessIdempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var handler = new SaveJobAdCommandHandler(db, _currentUser, _clock, _inspector);

        var first = await handler.Handle(new SaveJobAdCommand(jobAd.Id.Value), ct);
        var second = await handler.Handle(new SaveJobAdCommand(jobAd.Id.Value), ct);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        (await db.SavedJobAds.CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WhenJobAdMissing_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        _ = await SeedAsync(db, ct);
        var handler = new SaveJobAdCommandHandler(db, _currentUser, _clock, _inspector);

        var result = await handler.Handle(new SaveJobAdCommand(Guid.NewGuid()), ct);

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
        var handler = new SaveJobAdCommandHandler(db, currentUser, _clock, _inspector);

        var result = await handler.Handle(new SaveJobAdCommand(jobAd.Id.Value), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedJobAd.Unauthorized");
    }
}
