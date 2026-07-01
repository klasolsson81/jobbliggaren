using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Queries;

// ADR 0062 — RunSavedSearchQueryHandler är efter FTS-skiftet en tunn adapter
// kring IJobAdSearchQuery. Auth/cross-tenant-logiken (UserId-null, okänt id,
// cross-user) är OFÖRÄNDRAD och rör inte sök-kompositionen → de testas här mot
// in-memory-DB. Sök-kompositionen (filter/FTS/sort/paginering) testas mot
// riktig Postgres i Api.IntegrationTests. Porten mockas med NSubstitute.
public class RunSavedSearchQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly Guid _userId = Guid.NewGuid();

    public RunSavedSearchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static PagedResult<JobAdDto> EmptyPage(int page = 1, int pageSize = 20) =>
        new([], 0, page, pageSize);

    private static async Task<(JobSeeker seeker, SavedSearch saved)> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId,
        string? occupationGroup = null, string? municipality = null,
        string? region = null, string? q = null, string? employer = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        // Default: alla null kräver minst ett kriterium → occupationGroup
        // default. Single-element-lista ⇒ samma beteende som gammalt
        // single-värde (ADR 0039 Beslut 1 SPOT — regressions-grind).
        var groupList = occupationGroup is not null
            ? new[] { occupationGroup }
            : q is null && region is null && municipality is null
                ? ["grp_12345"] : System.Array.Empty<string>();
        var municipalityList = municipality is not null
            ? new[] { municipality } : System.Array.Empty<string>();
        var regionList = region is not null ? new[] { region } : System.Array.Empty<string>();
        var employerList = employer is not null ? new[] { employer } : System.Array.Empty<string>();
        var criteria = SearchCriteria.Create(
            occupationGroup: groupList,
            municipality: municipalityList,
            region: regionList,
            employmentType: null,
            worktimeExtent: null, employer: employerList,
            q: q,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        var saved = SavedSearch.Create(seeker.Id, "Kör mig", criteria, false,
            FakeDateTimeProvider.Default).Value;
        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, saved);
    }

    [Fact]
    public async Task Handle_WhenOwned_ReturnsPagedResultFromPort()
    {
        // Med mockad port spelar seedade JobAds ingen roll: handlern ska
        // returnera exakt det porten ger tillbaka.
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId, q: "backend");
        var dto = new JobAdDto(
            Guid.NewGuid(), "Backend-utvecklare", "Klarna", "Beskrivning",
            "https://example.com/1", "Manual", "Active",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow);
        var portResult = new PagedResult<JobAdDto>([dto], totalCount: 1, page: 1, pageSize: 20);
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(portResult);

        var handler = new RunSavedSearchQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), _search);

        var result = await handler.Handle(
            new RunSavedSearchQuery(saved.Id.Value), CancellationToken.None);

        result.ShouldBeSameAs(portResult);
        result!.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public async Task Handle_WhenOwned_MapsSearchCriteriaVoToJobAdSearchCriteria()
    {
        // C2 (architect F6): SearchCriteria-VO → JobAdSearchCriteria —
        // OccupationGroup/Municipality/Region/Q genomförda (täpper C1:s tomma
        // listor: tidigare skickades OccupationGroup: [] / Municipality: []),
        // Page/PageSize från queryn. #293/#306: Since-fältet utgår ur criteria
        // (NY beräknas på FE; en körning exponerade ändå aldrig "Ny").
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId,
            occupationGroup: "grp_12345", municipality: "sthlm_kn",
            region: "stockholm", q: "backend", employer: "5566010101");
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage(page: 2, pageSize: 5));

        var handler = new RunSavedSearchQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), _search);

        await handler.Handle(
            new RunSavedSearchQuery(saved.Id.Value, Page: 2, PageSize: 5),
            CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.Filter.OccupationGroup.ShouldBe(["grp_12345"]);
        captured.Filter.Municipality.ShouldBe(["sthlm_kn"]);
        captured.Filter.Region.ShouldBe(["stockholm"]);
        // #311 PR-2b C1: the SavedSearch's employer (org.nr) is reproduced into the filter on run
        // (the CONTAINED-seam replacement — a regression back to Employer: [] would fail here).
        captured.Filter.Employer.ShouldBe(["5566010101"]);
        captured.Filter.Q.ShouldBe("backend");
        captured.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        captured.Page.ShouldBe(2);
        captured.PageSize.ShouldBe(5);
    }

    [Fact]
    public async Task Handle_DoesNotWriteLastRunAt()
    {
        // ADR 0039 Beslut 2: run är en QUERY utan skriv-sidoeffekt.
        // last_run_at-skrivlogiken tillhör Fas 5 — får inte sättas i Fas 2.
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        saved.LastRunAt.ShouldBeNull(); // utgångsläge
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());

        var handler = new RunSavedSearchQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), _search);

        await handler.Handle(new RunSavedSearchQuery(saved.Id.Value), CancellationToken.None);

        // Reload från context — ingen skrivning ska ha skett.
        var reloaded = db.SavedSearches.Single(s => s.Id == saved.Id);
        reloaded.LastRunAt.ShouldBeNull();
        // UpdatedAt får inte heller röras av en ren körning.
        reloaded.UpdatedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, saved) = await SeedAsync(db, _userId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new RunSavedSearchQueryHandler(
            db, currentUser, Substitute.For<IFailedAccessLogger>(), _search);

        var result = await handler.Handle(
            new RunSavedSearchQuery(saved.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenIdUnknown_ReturnsNullAndDoesNotLog()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId);
        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new RunSavedSearchQueryHandler(
            db, _currentUser, failedAccessLogger, _search);

        var result = await handler.Handle(
            new RunSavedSearchQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenBelongsToOtherUser_ReturnsNullAndLogs()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherSaved) = await SeedAsync(db, otherUserId);
        var ownSeeker = JobSeeker.Register(_userId, "Current", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new RunSavedSearchQueryHandler(
            db, _currentUser, failedAccessLogger, _search);

        var result = await handler.Handle(
            new RunSavedSearchQuery(otherSaved.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "SavedSearch", otherSaved.Id.Value, _userId, "RunSavedSearch");
    }
}
