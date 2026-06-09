using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Application.RecentJobSearches.Queries.ListRecentSearches;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.RecentJobSearches;
using JobbPilot.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.RecentJobSearches.Queries;

// ADR 0062 — ListRecentSearchesQueryHandler hämtar live-träffräkningen via
// IJobAdSearchQuery.CountAsync (delad filter-SPOT med ListJobAds). Porten
// mockas med NSubstitute; list-projektion + label-härledning + owner-filter
// testas här mot in-memory-DB.
//
// C2 (ADR 0067, CTO-dom (d)/(e) + architect F5/F6): handlern mappar
// r.OccupationGroup/r.Municipality/r.Region/r.Q in i JobAdFilterCriteria
// (täpper C1:s tomma listor), resolvar occupationGroupLabels +
// municipalityLabels, och DeriveLabel-fallback är q → yrkesgrupp → kommun →
// region → "Alla annonser". DTO:n är ADDITIV: deprecated SsykList/SsykLabels
// matas ALLTID med [].
public class ListRecentSearchesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly Guid _userId = Guid.NewGuid();

    public ListRecentSearchesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
#pragma warning disable CA2012 // ValueTask från NSubstitute-stub konsumeras varje gång av handlern
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IReadOnlyList<string>>();
                IReadOnlyList<TaxonomyLabelDto> labels = ids
                    .Select(id => new TaxonomyLabelDto(id, $"Label-{id}"))
                    .ToList();
                return ValueTask.FromResult(labels);
            });
        // Default: CountAsync → 0 så NewCount-cap-testet (CurrentCount==0)
        // består. Enskilda tester override:ar vid behov.
        _search.CountAsync(Arg.Any<JobAdFilterCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(0));
#pragma warning restore CA2012
    }

    private async Task<JobSeeker> SeedSeekerAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static RecentJobSearch CaptureRow(
        JobSeekerId seekerId,
        string? q,
        DateTimeOffset viewedAt,
        int lastSeenCount = 0)
    {
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            q: q,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        return RecentJobSearch.Capture(seekerId, criteria, lastSeenCount, viewedAt);
    }

    [Fact]
    public async Task Handle_WhenUserIdNull_ReturnsEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new ListRecentSearchesQueryHandler(db, currentUser, _taxonomy, _search);

        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoSeeker_ReturnsEmpty()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);

        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsItemsSortedByLastViewedAtDesc()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var now = FakeDateTimeProvider.Default.UtcNow;

        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "oldest", now.AddHours(-3)));
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "newest", now));
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "middle", now.AddHours(-1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.Count.ShouldBe(3);
        result[0].Q.ShouldBe("newest");
        result[1].Q.ShouldBe("middle");
        result[2].Q.ShouldBe("oldest");
    }

    [Fact]
    public async Task Handle_ProjectsNewCountAsCurrentMinusLastSeen_CappedAtZero()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var now = FakeDateTimeProvider.Default.UtcNow;

        // CurrentCount kommer från IJobAdSearchQuery.CountAsync (default-stub = 0).
        // LastSeenCount lagrat på aggregatet — om större än CurrentCount så NewCount = 0.
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "with-seen", now, lastSeenCount: 5));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.CurrentCount.ShouldBe(0);          // port-stub = 0
        dto.NewCount.ShouldBe(0);              // max(0, 0 - 5)
    }

    [Fact]
    public async Task Handle_PropagatesPortCurrentCountToDto()
    {
        // ADR 0062 — live-count kommer från IJobAdSearchQuery.CountAsync.
        // Stubba porten → 7 och verifiera att CurrentCount når DTO:n samt att
        // NewCount = max(0, 7 - LastSeenCount).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var now = FakeDateTimeProvider.Default.UtcNow;
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "with-count", now, lastSeenCount: 2));
        await db.SaveChangesAsync(CancellationToken.None);
#pragma warning disable CA2012
        _search.CountAsync(Arg.Any<JobAdFilterCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(7));
#pragma warning restore CA2012

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.CurrentCount.ShouldBe(7);
        dto.NewCount.ShouldBe(5);              // max(0, 7 - 2)
    }

    [Fact]
    public async Task Handle_CallsCountAsyncWithRowFilterCriteria()
    {
        // ADR 0062 SPOT + C2 architect F6 — CountAsync ska anropas med radens
        // EGNA OccupationGroup/Municipality/Region/Q (täpper C1:s tomma listor:
        // tidigare skickades OccupationGroup: [] / Municipality: []).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_54321"],
            municipality: ["gbg_kn"],
            region: ["goteborg"],
            q: "lärare",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);
        JobAdFilterCriteria? captured = null;
#pragma warning disable CA2012
        _search.CountAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(0));
#pragma warning restore CA2012

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.OccupationGroup.ShouldBe(["grp_54321"]);
        captured.Municipality.ShouldBe(["gbg_kn"]);
        captured.Region.ShouldBe(["goteborg"]);
        captured.Q.ShouldBe("lärare");
    }

    // ---------------------------------------------------------------
    // DTO-projektion — additiv form (architect F5): deprecated SsykList/
    // SsykLabels ALLTID tomma; nya OccupationGroup-/Municipality-fält bär data.
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ProjectsOccupationGroupAndMunicipalityListsAndLabels()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "backend", FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.OccupationGroupList.ShouldBe(["grp_12345"]);
        dto.MunicipalityList.ShouldBe(["sthlm_kn"]);
        dto.OccupationGroupLabels.ShouldContain(l =>
            l.ConceptId == "grp_12345" && l.Label == "Label-grp_12345");
        dto.MunicipalityLabels.ShouldContain(l =>
            l.ConceptId == "sthlm_kn" && l.Label == "Label-sthlm_kn");
        dto.RegionList.ShouldBe(["stockholm"]);
    }

    [Fact]
    public async Task Handle_ProjectsDeprecatedSsykFieldsAsAlwaysEmpty()
    {
        // F5-kontraktet: FE-zod kräver ssykList (REQUIRED) → fältet består men
        // matas ALLTID med []. Tas bort i Fas E tillsammans med zod-schemat.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "backend", FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        var dto = result.ShouldHaveSingleItem();
        dto.SsykList.ShouldNotBeNull();
        dto.SsykList.ShouldBeEmpty();
        dto.SsykLabels.ShouldNotBeNull();
        dto.SsykLabels.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // DeriveLabel — fallback-ordning q → yrkesgrupp → kommun → region →
    // "Alla annonser" (architect F6)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_DerivesLabelFromQuery_WhenQPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "backend dev", FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("backend dev");
    }

    [Fact]
    public async Task Handle_DerivesLabelFromFirstOccupationGroupLabel_WhenQNull()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        // Q=null + yrkesgrupp + kommun + region → label från occupationGroupLabel
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_77777"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-grp_77777");
    }

    [Fact]
    public async Task Handle_DerivesLabelFromFirstMunicipalityLabel_WhenQAndOccupationGroupMissing()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["gbg_kn"],
            region: ["goteborg"],
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-gbg_kn");
    }

    [Fact]
    public async Task Handle_DerivesLabelFromFirstRegionLabel_WhenOnlyRegionPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: null,
            region: ["stockholm"],
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-stockholm");
    }

    [Fact]
    public async Task Handle_FiltersToOwnerOnly_CrossUserRowsExcluded()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var now = FakeDateTimeProvider.Default.UtcNow;

        // Egen rad
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "mine", now));

        // Annan användare
        var otherSeeker = JobSeeker.Register(Guid.NewGuid(), "Other", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(otherSeeker);
        db.RecentJobSearches.Add(CaptureRow(otherSeeker.Id, "theirs", now));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Q.ShouldBe("mine");
    }
}
