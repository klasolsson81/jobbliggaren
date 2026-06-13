using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.SavedSearches.Queries.GetSavedSearch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Queries;

public class GetSavedSearchQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetSavedSearchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, SavedSearch saved)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: ["sthlm_kn"],
            region: ["stockholm"], employmentType: null, worktimeExtent: null,
            q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Mitt sök", criteria, true,
            FakeDateTimeProvider.Default).Value;
        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, saved);
    }

    [Fact]
    public async Task Handle_WhenOwned_ReturnsDto()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new GetSavedSearchQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new GetSavedSearchQuery(saved.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(saved.Id.Value);
        result.Name.ShouldBe("Mitt sök");
        // C2 (architect F5.5/F6): DTO.OccupationGroup/Municipality projiceras
        // från VO:ns IReadOnlyList<string>. Single seeded element ⇒
        // ett-element-lista.
        result.OccupationGroup.ShouldBe(["grp_12345"]);
        result.Municipality.ShouldBe(["sthlm_kn"]);
        result.NotificationEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenIdUnknown_ReturnsNullAndDoesNotLog()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetSavedSearchQueryHandler(db, _currentUser, failedAccessLogger);

        var result = await handler.Handle(
            new GetSavedSearchQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new GetSavedSearchQueryHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new GetSavedSearchQuery(saved.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenBelongsToOtherUser_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetSavedSearchQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new GetSavedSearchQuery(otherSaved.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenBelongsToOtherUser_LogsCrossUserAttempt()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetSavedSearchQueryHandler(db, _currentUser, failedAccessLogger);

        await handler.Handle(
            new GetSavedSearchQuery(otherSaved.Id.Value), CancellationToken.None);

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "SavedSearch", otherSaved.Id.Value, _userId, "GetSavedSearch");
    }
}
