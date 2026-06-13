using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UserStatus.Queries.HasApplied;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.UserStatus;

public class HasAppliedQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public HasAppliedQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private async Task<(JobSeeker seeker, JobAd jobAd)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(_userId, "Test", _clock).Value;
        db.JobSeekers.Add(seeker);
        var jobAd = JobAd.Create(
            "Test", Company.Create("Acme").Value, "Desc", "https://example.com",
            JobSource.Manual, _clock.UtcNow, _clock.UtcNow.AddDays(30), _clock).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return (seeker, jobAd);
    }

    [Fact]
    public async Task Handle_NoApplication_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var handler = new HasAppliedQueryHandler(db, _currentUser);

        var result = await handler.Handle(new HasAppliedQuery(jobAd.Id.Value), ct);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WithApplication_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (seeker, jobAd) = await SeedAsync(db, ct);
        var app = Jobbliggaren.Domain.Applications.Application
            .Create(seeker.Id, jobAd.Id, null, null, _clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        var handler = new HasAppliedQueryHandler(db, _currentUser);
        var result = await handler.Handle(new HasAppliedQuery(jobAd.Id.Value), ct);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_AnonymousUser_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var (_, jobAd) = await SeedAsync(db, ct);
        var anonUser = Substitute.For<ICurrentUser>();
        anonUser.UserId.Returns((Guid?)null);
        var handler = new HasAppliedQueryHandler(db, anonUser);

        var result = await handler.Handle(new HasAppliedQuery(jobAd.Id.Value), ct);

        result.ShouldBeFalse();
    }
}
