using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Queries;

// ADR 0062 — ListRecentSearchesQueryHandler hämtar live-träffräkningen via
// IJobAdSearchQuery.CountAsync (delad filter-SPOT med ListJobAds). Porten
// mockas med NSubstitute; list-projektion + label-härledning + owner-filter
// testas här mot in-memory-DB.
//
// C2 (ADR 0067, CTO-dom (d)/(e) + architect F5/F6): handlern mappar
// r.OccupationGroup/r.Municipality/r.Region/r.Q in i JobAdFilterCriteria
// (täpper C1:s tomma listor), resolvar occupationGroupLabels +
// municipalityLabels, och DeriveLabel-fallback är q → yrkesgrupp → kommun →
// region → "Alla annonser". E2b: C2-shimmet (SsykList/SsykLabels) borttaget
// ur DTO:n — vakthund i RecentJobSearchDtoContractTests.
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
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db)
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
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
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
            employmentType: null,
            worktimeExtent: null, employer: ["5566010101"], remote: false,
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
        // #311 PR-2b C1: the recent row's employer (org.nr) is reproduced into the count filter
        // (the CONTAINED-seam replacement — a regression back to Employer: [] would fail here).
        captured.Employer.ShouldBe(["5566010101"]);
        captured.Q.ShouldBe("lärare");
    }

    [Fact]
    public async Task Handle_ThreadsRowRemoteIntoCountFilter()
    {
        // #551 PR-D: the recent row's remote (distans) flag is reproduced into the per-row count
        // filter (the CONTAINED-seam replacement — a regression back to Remote: false fails here).
        // A remote-only row is a valid search (empty-invariant accepts remote=true).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var criteria = SearchCriteria.Create(
            occupationGroup: null, municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null, remote: true,
            q: null,
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
        captured!.Remote.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // DTO-projektion — slutgiltig E2b-form (C2-shimmet SsykList/SsykLabels
    // borttaget; vakthund i RecentJobSearchDtoContractTests).
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
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
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
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
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
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-stockholm");
    }

    // ---------------------------------------------------------------
    // E2g (Klas-direktiv 2026-06-11) — DeriveLabel: hel-områdes-kollaps +
    // "+N till" (CTO-bekräftad mekanik; tree = in-memory-snapshot).
    // ---------------------------------------------------------------

    private void StubTree(params TaxonomyOccupationFieldDto[] fields)
    {
#pragma warning disable CA2012
        _taxonomy.GetTreeAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new TaxonomyTreeDto(
                Regions: [],
                OccupationFields: fields,
                EmploymentTypes: [],
                WorktimeExtents: [])));
#pragma warning restore CA2012
    }

    private static TaxonomyOccupationFieldDto Field(
        string conceptId, string label, params string[] groupIds) =>
        new(
            conceptId,
            label,
            Occupations: [],
            OccupationGroups: groupIds
                .Select(id => new TaxonomyOccupationGroupDto(id, $"Label-{id}"))
                .ToList());

    private static RecentJobSearch GroupsRow(
        JobSeekerId seekerId, IReadOnlyList<string> groups)
    {
        var criteria = SearchCriteria.Create(
            occupationGroup: groups,
            municipality: null,
            region: null,
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        return RecentJobSearch.Capture(
            seekerId, criteria, 0, FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task Handle_DerivesFieldLabel_WhenSelectionIsExactlyOneWholeField()
    {
        // (i): exakt alla grupper i ETT yrkesområde → områdets namn ("Data/IT"),
        // inte första gruppens (Klas-buggen: "Drifttekniker, IT" vid helt område).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        StubTree(Field("falt_datait", "Data/IT", "grp_a", "grp_b", "grp_c"));

        // Sorterad+distinct-normalisering i VO:t — ordningen här är irrelevant.
        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_c", "grp_a", "grp_b"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Data/IT");
    }

    [Fact]
    public async Task Handle_DerivesPlusNLabel_WhenMultipleGroupsNotWholeField()
    {
        // (iii): flera grupper som INTE är ett helt område → "{första} +N till".
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        StubTree(Field("falt_datait", "Data/IT", "grp_a", "grp_b", "grp_c"));

        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_a", "grp_b"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-grp_a +1 till");
    }

    [Fact]
    public async Task Handle_DerivesPlusNLabel_WhenWholeFieldPlusExtraGroup()
    {
        // Blandfall (CTO-fallgrop c): helt område + extra grupp från annat →
        // (iii) räknat på GRUPPER, aldrig blandade enheter.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        StubTree(
            Field("falt_datait", "Data/IT", "grp_a", "grp_b"),
            Field("falt_bygg", "Bygg och anläggning", "grp_x"));

        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_a", "grp_b", "grp_x"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-grp_a +2 till");
    }

    [Fact]
    public async Task Handle_DerivesPlusNLabel_ForMultipleMunicipalities()
    {
        // Samma +N-mönster för kommuner (CTO-extrapolering av direktivet).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["kn_a", "kn_b", "kn_c"],
            region: null,
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(RecentJobSearch.Capture(
            seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-kn_a +2 till");
    }

    [Fact]
    public async Task Handle_FallsBackToPlusN_WhenTreeHasNoMatchingFields()
    {
        // Taxonomi-drift/degradering (CTO-fallgrop): trädet finns men
        // selektionen matchar inget fält (tomt fält-set = degraderad
        // snapshot) → (i)-matchen faller gracefully till (iii). Aldrig
        // krasch, aldrig hårdkodade antal. (Null-träd är kontrakts-omöjligt
        // per ITaxonomyReadModel — code-reviewer Minor 3 E2g.)
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        StubTree(); // tomt fält-set

        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_a", "grp_b"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-grp_a +1 till");
    }

    [Fact]
    public async Task Handle_DerivesPlusNLabel_ForMultipleRegions()
    {
        // Region-grenen får samma +N-mönster (code-reviewer Minor 4 —
        // WithMoreSuffix delas men grenen ska vara test-låst).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: null,
            region: ["reg_a", "reg_b"],
            employmentType: null,
            worktimeExtent: null, employer: null, remote: false,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(RecentJobSearch.Capture(
            seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-reg_a +1 till");
    }

    [Fact]
    public async Task Handle_FetchesTreeExactlyOnce_AndOnlyWhenMultiGroupRowsExist()
    {
        // CTO-kravet (en gång per Handle) + gaten test-låsta
        // (code-reviewer Minor 4 — gaten var obevakad).
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        StubTree(Field("falt_x", "Fält X", "grp_a", "grp_b"));

        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_a", "grp_b"]));
        db.RecentJobSearches.Add(GroupsRow(seeker.Id, ["grp_a"]));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        await _taxonomy.Received(1).GetTreeAsync(Arg.Any<CancellationToken>());

        // Enbart ≤1-grupps-rader → trädet hämtas INTE.
        _taxonomy.ClearReceivedCalls();
        var db2 = TestAppDbContextFactory.Create();
        var seeker2 = await SeedSeekerAsync(db2);
        db2.RecentJobSearches.Add(GroupsRow(seeker2.Id, ["grp_a"]));
        await db2.SaveChangesAsync(CancellationToken.None);

        var handler2 = new ListRecentSearchesQueryHandler(db2, _currentUser, _taxonomy, _search);
        await handler2.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        await _taxonomy.DidNotReceive().GetTreeAsync(Arg.Any<CancellationToken>());
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
