using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.SavedSearches.Commands.CreateSavedSearch;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Commands;

public class CreateSavedSearchCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public CreateSavedSearchCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // C2 (architect F6): Ssyk → OccupationGroup + Municipality. Named args
    // (tre likatypade listor i rad).
    private static CreateSavedSearchCommand ValidCommand() =>
        new("Backend i Stockholm",
            OccupationGroup: ["grp_12345"],
            Municipality: ["sthlm_kn"],
            Region: ["stockholm"],
            EmploymentType: null,
            WorktimeExtent: null,
            Q: "backend",
            SortBy: JobAdSortBy.PublishedAtDesc,
            NotificationEnabled: true);

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithNewId()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new CreateSavedSearchCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
        // SaveChanges görs av UnitOfWork pipeline-behavior (CLAUDE.md §2.3),
        // inte av handlern — entiteten är Added men inte committad i unit-scope.
        db.SavedSearches.Local.Count.ShouldBe(1);
        db.SavedSearches.Local.Single().Id.Value.ShouldBe(result.Value);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new CreateSavedSearchCommandHandler(db, currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new CreateSavedSearchCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WithInvalidCriteria_ReturnsDomainValidationError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new CreateSavedSearchCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        // Inget kriterium angivet → SearchCriteria.Empty bubblar upp som 400.
        var command = new CreateSavedSearchCommand(
            "Tom sökning",
            OccupationGroup: null,
            Municipality: null,
            Region: null,
            EmploymentType: null,
            WorktimeExtent: null,
            Q: null,
            SortBy: JobAdSortBy.PublishedAtDesc,
            NotificationEnabled: false);
        // OBS: tomma/null-listor + null Q = generaliserad tom-invariant
        // (ADR 0042 Beslut B.3) → samma SearchCriteria.Empty som gammalt.

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
        db.SavedSearches.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsDomainNameRequiredError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new CreateSavedSearchCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new CreateSavedSearchCommand(
            "",
            OccupationGroup: ["grp_12345"],
            Municipality: null,
            Region: null,
            EmploymentType: null,
            WorktimeExtent: null,
            Q: null,
            SortBy: JobAdSortBy.PublishedAtDesc,
            NotificationEnabled: false);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.NameRequired");
    }
}
