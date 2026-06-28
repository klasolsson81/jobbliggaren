using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetJobsWatermark;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — reads the authenticated user's <c>LastSeenJobsAt</c>
/// watermark. Owner-scoped. Contract:
/// <list type="bullet">
/// <item>no authenticated user → null watermark (anon gets no NY);</item>
/// <item>authenticated but no JobSeeker → null;</item>
/// <item>never visited → null (FE shows no NY on the first visit);</item>
/// <item>visited → the persisted timestamp.</item>
/// </list>
/// </summary>
public class GetJobsWatermarkQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public GetJobsWatermarkQueryHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    private static ICurrentUser UserWith(Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private JobSeeker SeedSeeker(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", _clock).Value;
        db.JobSeekers.Add(seeker);
        db.SaveChanges();
        return seeker;
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenNoAuthenticatedUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new GetJobsWatermarkQueryHandler(db, UserWith(null));

        var result = await sut.Handle(new GetJobsWatermarkQuery(), ct);

        result.LastSeenJobsAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenNoJobSeekerForUser()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create();
        var sut = new GetJobsWatermarkQueryHandler(db, UserWith(Guid.NewGuid()));

        var result = await sut.Handle(new GetJobsWatermarkQuery(), ct);

        result.LastSeenJobsAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenNeverVisited()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        SeedSeeker(db, userId);

        var sut = new GetJobsWatermarkQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetJobsWatermarkQuery(), ct);

        result.LastSeenJobsAt.ShouldBeNull("a fresh seeker has never visited /jobb");
    }

    [Fact]
    public async Task Handle_ShouldReturnWatermark_WhenVisited()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, userId);
        seeker.SetLastSeenJobs(_clock);
        db.SaveChanges();

        var sut = new GetJobsWatermarkQueryHandler(db, UserWith(userId));

        var result = await sut.Handle(new GetJobsWatermarkQuery(), ct);

        result.LastSeenJobsAt.ShouldBe(Now);
    }
}
