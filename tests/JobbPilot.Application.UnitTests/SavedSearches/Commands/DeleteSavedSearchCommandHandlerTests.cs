using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.SavedSearches.Commands.DeleteSavedSearch;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Commands;

public class DeleteSavedSearchCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public DeleteSavedSearchCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, SavedSearch saved)> SeedAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: null, region: null,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Att radera", criteria, false,
            FakeDateTimeProvider.Default).Value;
        db.SavedSearches.Add(saved);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, saved);
    }

    [Fact]
    public async Task Handle_WithValidCommand_SoftDeletesSavedSearch()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new DeleteSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteSavedSearchCommand(saved.Id.Value), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        saved.DeletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Handle_WhenAlreadyDeleted_IsIdempotentSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new DeleteSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var first = await handler.Handle(
            new DeleteSavedSearchCommand(saved.Id.Value), CancellationToken.None);
        var firstDeletedAt = saved.DeletedAt;
        var second = await handler.Handle(
            new DeleteSavedSearchCommand(saved.Id.Value), CancellationToken.None);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        saved.DeletedAt.ShouldBe(firstDeletedAt); // SoftDelete no-op vid upprepning
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new DeleteSavedSearchCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new DeleteSavedSearchCommand(saved.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenIdUnknown_ReturnsNotFoundAndDoesNotLog()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new DeleteSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);

        var result = await handler.Handle(
            new DeleteSavedSearchCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenSavedSearchBelongsToOtherUser_ReturnsNotFoundAndLogs()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new DeleteSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);

        var result = await handler.Handle(
            new DeleteSavedSearchCommand(otherSaved.Id.Value), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "SavedSearch", otherSaved.Id.Value, _userId, "DeleteSavedSearch");
        otherSaved.DeletedAt.ShouldBeNull(); // ej raderad — cross-tenant nekad
    }
}
