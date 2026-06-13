using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.SavedJobAds.Commands.UnsaveJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedJobAds.Commands;

public class UnsaveJobAdCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public UnsaveJobAdCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private async Task<(JobSeeker seeker, SavedJobAd saved, JobAdId jobAdId)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);
        var jobAdId = new JobAdId(Guid.NewGuid());
        var saved = SavedJobAd.Save(seeker.Id, jobAdId, _clock.UtcNow);
        db.SavedJobAds.Add(saved);
        await db.SaveChangesAsync(ct);
        return (seeker, saved, jobAdId);
    }

    [Fact]
    public async Task Handle_WhenSaved_RemovesRecord()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, _, jobAdId) = await SeedAsync(db, ct);
        var handler = new UnsaveJobAdCommandHandler(db, _currentUser, _clock);

        var result = await handler.Handle(new UnsaveJobAdCommand(jobAdId.Value), ct);

        result.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        (await db.SavedJobAds.AnyAsync(ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenNotSaved_ReturnsSuccessIdempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        _ = await SeedAsync(db, ct);
        var handler = new UnsaveJobAdCommandHandler(db, _currentUser, _clock);

        var result = await handler.Handle(new UnsaveJobAdCommand(Guid.NewGuid()), ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, _, jobAdId) = await SeedAsync(db, ct);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new UnsaveJobAdCommandHandler(db, currentUser, _clock);

        var result = await handler.Handle(new UnsaveJobAdCommand(jobAdId.Value), ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedJobAd.Unauthorized");
    }
}
