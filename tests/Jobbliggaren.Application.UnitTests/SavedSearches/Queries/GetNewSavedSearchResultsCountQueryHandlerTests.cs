using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.SavedSearches.Queries.GetNewResultsCount;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Queries;

/// <summary>
/// #312 (ADR 0115) — the per-saved-search "N nya träffar"-count handler. A thin owner-scoped adapter
/// that fans out over the caller's NotificationEnabled searches and counts each via
/// <see cref="IJobAdSearchQuery.CountNewSinceAsync"/> (mocked here; the real windowed / synonym /
/// Active-only count is Testcontainers-tested at the port). Contract: unauth / no seeker → empty;
/// only NotificationEnabled searches count; the search's <c>ResultsSeenAt</c> is passed as
/// <c>since</c>; soft-deleted searches are excluded (query filter); the fan-out is capped (R1/DoS).
/// </summary>
public class GetNewSavedSearchResultsCountQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetNewSavedSearchResultsCountQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static SearchCriteria Criteria() =>
        SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null, remote: false,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;

    private static JobSeeker SeedSeeker(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        db.SaveChanges();
        return seeker;
    }

    private static SavedSearch AddSearch(AppDbContext db, JobSeekerId seekerId, string name, bool notify)
    {
        var saved = SavedSearch.Create(seekerId, name, Criteria(), notify, FakeDateTimeProvider.Default).Value;
        db.SavedSearches.Add(saved);
        db.SaveChanges();
        return saved;
    }

    private GetNewSavedSearchResultsCountQueryHandler Sut(AppDbContext db) =>
        new(db, _currentUser, _search);

    private void PortReturns(int count) =>
        _search.CountNewSinceAsync(
                Arg.Any<JobAdFilterCriteria>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(count);

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoAuthenticatedUser()
    {
        using var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var result = await new GetNewSavedSearchResultsCountQueryHandler(db, currentUser, _search)
            .Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoJobSeeker()
    {
        using var db = TestAppDbContextFactory.Create();

        var result = await Sut(db).Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_CountsOnlyNotificationEnabledSearches()
    {
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, _userId);
        var notifyOn = AddSearch(db, seeker.Id, "On", notify: true);
        AddSearch(db, seeker.Id, "Off", notify: false);   // excluded from the due-set
        PortReturns(7);

        var result = await Sut(db).Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.SavedSearchId.ShouldBe(notifyOn.Id.Value);
        dto.Name.ShouldBe("On");
        dto.NewCount.ShouldBe(7);
    }

    [Fact]
    public async Task Handle_PassesResultsSeenAtAsSince()
    {
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, _userId);
        var saved = AddSearch(db, seeker.Id, "On", notify: true);
        DateTimeOffset? capturedSince = null;
        _search.CountNewSinceAsync(
                Arg.Any<JobAdFilterCriteria>(),
                Arg.Do<DateTimeOffset>(s => capturedSince = s),
                Arg.Any<CancellationToken>())
            .Returns(0);

        await Sut(db).Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        // The per-search watermark drives the window — NOT clock-now (watermark-model, #293/#306).
        capturedSince.ShouldBe(saved.ResultsSeenAt);
    }

    [Fact]
    public async Task Handle_ExcludesSoftDeletedSearches()
    {
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, _userId);
        var live = AddSearch(db, seeker.Id, "Live", notify: true);
        var deleted = AddSearch(db, seeker.Id, "Deleted", notify: true);
        deleted.SoftDelete(FakeDateTimeProvider.Default);
        db.SaveChanges();
        PortReturns(3);

        var result = await Sut(db).Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().SavedSearchId.ShouldBe(live.Id.Value);
    }

    [Fact]
    public async Task Handle_CapsFanOutAtTwenty()
    {
        using var db = TestAppDbContextFactory.Create();
        var seeker = SeedSeeker(db, _userId);
        for (var i = 0; i < 25; i++)
            AddSearch(db, seeker.Id, $"S{i}", notify: true);
        PortReturns(1);

        var result = await Sut(db).Handle(new GetNewSavedSearchResultsCountQuery(), CancellationToken.None);

        // The per-search COUNT fan-out is bounded (R1/DoS — SavedSearch has no per-seeker create
        // cap): at most 20 searches are scanned, so at most 20 DTOs come back.
        result.Count.ShouldBe(20);
    }
}
