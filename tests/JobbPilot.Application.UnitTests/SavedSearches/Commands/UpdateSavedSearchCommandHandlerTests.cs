using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.SavedSearches.Commands.UpdateSavedSearch;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Commands;

public class UpdateSavedSearchCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public UpdateSavedSearchCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, SavedSearch saved)> SeedAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: ["sthlm_kn"],
            region: ["stockholm"], q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Originalnamn", criteria, false,
            FakeDateTimeProvider.Default).Value;
        db.SavedSearches.Add(saved);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, saved);
    }

    [Fact]
    public async Task Handle_RenameOnly_UpdatesNameSuccessfully()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(saved.Id.Value, "Nytt namn", null, null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_CriteriaOnly_UpdatesCriteriaSuccessfully()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(saved.Id.Value, null, null,
                new SavedSearchCriteriaInput(
                    OccupationGroup: ["grp_99999"], Municipality: null,
                    Region: null, Q: null, SortBy: JobAdSortBy.PublishedAtAsc)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_NotificationOnly_UpdatesFlagSuccessfully()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(saved.Id.Value, null, true, null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidCriteria_ReturnsDomainValidationError()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(saved.Id.Value, null, null,
                new SavedSearchCriteriaInput(
                    OccupationGroup: ["has space"], Municipality: null,
                    Region: null, Q: null, SortBy: JobAdSortBy.PublishedAtDesc)),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidOccupationGroup");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new UpdateSavedSearchCommandHandler(
            db, currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(saved.Id.Value, "X", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenIdUnknown_ReturnsNotFoundAndDoesNotLog()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(Guid.NewGuid(), "X", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenSavedSearchBelongsToOtherUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        // current user måste ha egen JobSeeker — annars tidig-return innan
        // cross-tenant-detektion.
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(
            new UpdateSavedSearchCommand(otherSaved.Id.Value, "Hijack", null, null),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NotFound");
    }

    [Fact]
    public async Task Handle_WhenSavedSearchBelongsToOtherUser_LogsCrossUserAttempt()
    {
        // ADR 0031 / TD-67: ownership-mismatch loggas via IFailedAccessLogger.
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new UpdateSavedSearchCommandHandler(
            db, _currentUser, FakeDateTimeProvider.Default, failedAccessLogger);

        await handler.Handle(
            new UpdateSavedSearchCommand(otherSaved.Id.Value, "Hijack", null, null),
            CancellationToken.None);

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "SavedSearch", otherSaved.Id.Value, _userId, "UpdateSavedSearch");
    }
}
