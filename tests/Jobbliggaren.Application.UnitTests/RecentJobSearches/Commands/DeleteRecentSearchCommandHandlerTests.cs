using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.RecentJobSearches.Commands.DeleteRecentSearch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Commands;

public class DeleteRecentSearchCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public DeleteRecentSearchCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, RecentJobSearch recent)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: null, region: null,
            employmentType: null, worktimeExtent: null,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var recent = RecentJobSearch.Capture(
            seeker.Id, criteria, currentCount: 5, FakeDateTimeProvider.Default.UtcNow);
        db.RecentJobSearches.Add(recent);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, recent);
    }

    [Fact]
    public async Task Handle_WithOwnRecord_HardDeletes()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, recent) = await SeedAsync(db, _userId);
        var handler = new DeleteRecentSearchCommandHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteRecentSearchCommand(recent.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);
        db.RecentJobSearches.Any(r => r.Id == recent.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorized()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, recent) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new DeleteRecentSearchCommandHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteRecentSearchCommand(recent.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RecentJobSearch.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenRecordNotFound_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var handler = new DeleteRecentSearchCommandHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteRecentSearchCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RecentJobSearch.NotFound");
    }

    [Fact]
    public async Task Handle_WithOtherUsersRecord_LogsCrossUserAttempt_AndReturnsNotFound()
    {
        // Två separata seekers — den ena försöker ta bort den andras row.
        var db = TestAppDbContextFactory.Create();
        var ownerUserId = Guid.NewGuid();
        var (_, recent) = await SeedAsync(db, ownerUserId);

        // Aktuell user är annan (har ingen seeker än → lägg till en så lookup ger != default)
        var otherSeeker = JobSeeker.Register(_userId, "Other", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(otherSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new DeleteRecentSearchCommandHandler(db, _currentUser, failedLogger);

        var result = await handler.Handle(
            new DeleteRecentSearchCommand(recent.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RecentJobSearch.NotFound");
        failedLogger.Received(1).LogCrossUserAttempt(
            "RecentJobSearch", recent.Id.Value, _userId, "DeleteRecentSearch");
    }
}
