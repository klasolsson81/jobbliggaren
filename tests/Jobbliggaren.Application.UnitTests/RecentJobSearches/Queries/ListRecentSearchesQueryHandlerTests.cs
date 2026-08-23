using System.Reflection;
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
// (täpper C1:s tomma listor) och resolvar occupationGroupLabels +
// municipalityLabels. E2b: C2-shimmet (SsykList/SsykLabels) borttaget
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
                var ids = call.ArgAt<IReadOnlyList<string>>(0);
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
        int lastSeenCount = 0,
        bool remote = false)
    {
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            employmentType: null,
            worktimeExtent: null, employer: null, remote: remote,
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

    [Fact]
    public async Task Handle_ProjectsRowRemoteIntoDto_InBothPolarities()
    {
        // #1407: distans nådde count-filtret (Handle_ThreadsRowRemoteIntoCountFilter) men
        // inte projektionen, så "Kör igen" byggde en href UTAN distans för en rad vars
        // count räknats MED den. Båda polariteterna i EN körning: en konstant `Remote:
        // true` i projektionen består ett ensidigt test.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        var now = FakeDateTimeProvider.Default.UtcNow;

        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "distansjobb", now, remote: true));
        db.RecentJobSearches.Add(CaptureRow(seeker.Id, "kontorsjobb", now.AddHours(-1)));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        result.Single(d => d.Q == "distansjobb").Remote.ShouldBeTrue();
        result.Single(d => d.Q == "kontorsjobb").Remote.ShouldBeFalse();
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
    // DeriveLabel
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

    // Kommun och län är samma dimension i två granulariteter och unioneras i
    // geo-predikatet, så en rad med båda får båda i labeln. Att namna enbart
    // kommunen beskrev en strikt delmängd av vad klicket kör.
    [Fact]
    public async Task Handle_JoinsMunicipalityAndRegion_WhenBothPresent()
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

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-gbg_kn eller Label-goteborg");
    }

    [Fact]
    public async Task Handle_DerivesLabelFromMunicipality_WhenOnlyMunicipalityPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["gbg_kn"],
            region: null,
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

    // #1413 — distans ensam bar tidigare fallbacken "Alla annonser", som är
    // falsk: raden är inte alla annonser, den är distansannonser.
    [Fact]
    public async Task Handle_DerivesDistansLabel_WhenRemoteIsTheOnlyCriterion()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: null,
            region: null,
            employmentType: null,
            worktimeExtent: null, employer: null, remote: true,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Distans");
    }

    [Fact]
    public async Task Handle_JoinsMunicipalityAndRemote_WhenBothPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["gbg_kn"],
            region: null,
            employmentType: null,
            worktimeExtent: null, employer: null, remote: true,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-gbg_kn eller distans");
    }

    [Fact]
    public async Task Handle_JoinsAllThreeOrtGranularities_WithCommaBeforeFinalEller()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["gbg_kn"],
            region: ["goteborg"],
            employmentType: null,
            worktimeExtent: null, employer: null, remote: true,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label
            .ShouldBe("Label-gbg_kn, Label-goteborg eller distans");
    }

    [Fact]
    public async Task Handle_AppliesPlusNPerGranularity_NotAcrossTheJoinedOrtLabel()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var criteria = SearchCriteria.Create(
            occupationGroup: null,
            municipality: ["gbg_kn", "sthlm_kn"],
            region: null,
            employmentType: null,
            worktimeExtent: null, employer: null, remote: true,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe("Label-gbg_kn +1 till eller distans");
    }

    // #1418 — en rad som saknar primär dimension namnges av sina förfiningsfilter i stället
    // för "Alla annonser", som är falskt på samma sätt som #1413:s distans-fall. Ersätter
    // karakteriseringstestet som pinnade det gamla beteendet.
    //
    // KLASS-pin, inte instans-pin: en pin på employmentType ensam faller inte för
    // worktimeExtent eller employer. Ett fall per axel, och
    // Handle_LabelCoversEveryNarrowingAxis fäller om en axel saknar fall.
    //
    // Källmängden är SearchCriteria, inte RecentJobSearch: kriteriet är det labeln beskriver,
    // och VO:t bär inga identitets- eller besöksbokföringsfält att undanta — guarden behöver
    // EN uteslutning i stället för sju.
    private static readonly IReadOnlyDictionary<string, (SearchCriteria Criteria, string Label)>
        AxisCases = new Dictionary<string, (SearchCriteria, string)>(StringComparer.Ordinal)
        {
            ["Q"] = (Axis(q: "backend dev"), "backend dev"),
            ["OccupationGroup"] = (Axis(occupationGroup: ["grp_77777"]), "Label-grp_77777"),
            ["Municipality"] = (Axis(municipality: ["gbg_kn"]), "Label-gbg_kn"),
            ["Region"] = (Axis(region: ["stockholm"]), "Label-stockholm"),
            ["EmploymentType"] = (Axis(employmentType: ["tillsvidare"]), "Label-tillsvidare"),
            ["WorktimeExtent"] = (Axis(worktimeExtent: ["heltid"]), "Label-heltid"),
            ["Employer"] = (Axis(employer: ["5566010101"]), "Vald arbetsgivare"),
            ["Remote"] = (Axis(remote: true), "Distans"),
        };

    // SortBy SMALNAR inte: två rader som skiljer sig bara i sortering kör samma filter mot
    // samma annonser, så axeln kan inte namnge raden. Explicit uppräkning hellre än härledd
    // form, så tystnad FÄLLER i stället för att passera.
    private static readonly HashSet<string> NotNarrowing =
        new(StringComparer.Ordinal) { "SortBy" };

    private static SearchCriteria Axis(
        IReadOnlyList<string>? occupationGroup = null,
        IReadOnlyList<string>? municipality = null,
        IReadOnlyList<string>? region = null,
        IReadOnlyList<string>? employmentType = null,
        IReadOnlyList<string>? worktimeExtent = null,
        IReadOnlyList<string>? employer = null,
        bool remote = false,
        string? q = null) =>
        SearchCriteria.Create(
            occupationGroup: occupationGroup,
            municipality: municipality,
            region: region,
            employmentType: employmentType,
            worktimeExtent: worktimeExtent,
            employer: employer,
            remote: remote,
            q: q,
            sortBy: JobAdSortBy.PublishedAtDesc).Value;

    public static TheoryData<string> NarrowingAxes()
    {
        var data = new TheoryData<string>();
        foreach (var axis in AxisCases.Keys)
            data.Add(axis);
        return data;
    }

    [Theory]
    [MemberData(nameof(NarrowingAxes))]
    public async Task Handle_NamesTheRowByItsOnlySetAxis(string axis)
    {
        var (criteria, expected) = AxisCases[axis];
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(
            RecentJobSearch.Capture(seeker.Id, criteria, 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldBe(expected);
    }

    [Fact]
    public void Handle_LabelCoversEveryNarrowingAxis()
    {
        var axes = typeof(SearchCriteria)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !NotNarrowing.Contains(name))
            .ToArray();

        // Golv mot bruten källmängd: en inklusionsspec kan aldrig upptäcka att den mäter
        // ingenting — blir axes tom är uncovered tom också, och båda fakta passerar tysta.
        axes.ShouldNotBeEmpty(
            "guarden mäter ingenting om SearchCriteria inte exponerar några axlar");

        var uncovered = axes
            .Where(name => !AxisCases.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        uncovered.ShouldBeEmpty(
            "varje smalnande SearchCriteria-axel behöver ett fall i AxisCases, annars kan en "
            + "rad vars enda satta axel är den namnges \"Alla annonser\" utan att någon test "
            + $"faller. Saknas: {string.Join(", ", uncovered)}");

        var stale = AxisCases.Keys
            .Where(name => !axes.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        stale.ShouldBeEmpty(
            $"AxisCases namnger axlar SearchCriteria inte längre har: {string.Join(", ", stale)}");
    }

    // Varje satt förfiningsaxel räknas upp. Att namnge bara en av dem beskriver en äkta
    // ÖVERMÄNGD av vad klicket kör — spegelbilden av #1413:s ort-fall, där "Stockholm"
    // namngav en strikt delmängd. Kommat är fogningen (Klas-beslut 2026-08-23); ort-unionens
    // "eller" vore semantiskt falskt här, eftersom axlarna AND:as (JobAdSearchComposition).
    [Fact]
    public async Task Handle_EnumeratesEveryRefinementAxis_WhenSeveralAreSet()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(RecentJobSearch.Capture(
            seeker.Id,
            Axis(
                employmentType: ["tillsvidare", "vikariat"],
                worktimeExtent: ["heltid"],
                employer: ["5566010101"]),
            0,
            FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label
            .ShouldBe("Label-tillsvidare +1 till, Label-heltid, Vald arbetsgivare");
    }

    // Org.nr:et får aldrig nå labeln — den ÄR svarstext, och RecentSearchesTests assertar på
    // värdet i hela svarskroppen. Här pinnas samma sak en nivå ned, där labeln föds.
    [Fact]
    public async Task Handle_NeverPutsTheEmployerOrgNumberInTheLabel()
    {
        const string orgNr = "5566010101";
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(RecentJobSearch.Capture(
            seeker.Id, Axis(employer: [orgNr]), 0, FakeDateTimeProvider.Default.UtcNow));
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new ListRecentSearchesQueryHandler(db, _currentUser, _taxonomy, _search);
        var result = await handler.Handle(new ListRecentSearchesQuery(), CancellationToken.None);

        result.ShouldHaveSingleItem().Label.ShouldNotContain(orgNr);
    }

    // Regressionspin: en rad SOM HAR en primär dimension beter sig exakt som förut. ADR 0067:s
    // premiss — förfiningsfilter bär inte labeln — håller där den alltid hållit; #1418 rör bara
    // den klass premissen aldrig förutsåg.
    [Fact]
    public async Task Handle_LeavesTheLabelUnchanged_WhenAPrimaryDimensionIsPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);
        db.RecentJobSearches.Add(RecentJobSearch.Capture(
            seeker.Id,
            Axis(
                municipality: ["gbg_kn"],
                employmentType: ["tillsvidare"],
                worktimeExtent: ["heltid"]),
            0,
            FakeDateTimeProvider.Default.UtcNow));
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
