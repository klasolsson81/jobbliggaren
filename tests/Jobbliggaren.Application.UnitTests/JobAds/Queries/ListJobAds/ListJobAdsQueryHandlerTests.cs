using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.ListJobAds;

// ADR 0062 — ListJobAdsQueryHandler är efter FTS-skiftet en TUNN ADAPTER:
// den mappar ListJobAdsQuery → JobAdSearchCriteria och delegerar till
// IJobAdSearchQuery. Hela sök-kompositionen (ssyk/region-filter, q-FTS-hybrid,
// ts_rank-relevans, sortering, paginering, projektion) bor i Infrastructure-
// impl:en JobAdSearchQuery → testas mot riktig Postgres i
// Api.IntegrationTests/JobAds/ListJobAdsFtsTests.cs + ListJobAdsMultiFilterTests.cs.
//
// Dessa unit-tester verifierar ENBART adapter-kontraktet: korrekt mappning
// query→criteria och att port-resultatet returneras oförändrat. Porten mockas
// med NSubstitute — ingen DB.
//
// Fas D2 (ADR 0067 Beslut 5c) — ctor:n tar en RIKTIG SearchQueryParser (ren CPU,
// deterministisk, InternalsVisibleTo) — äkta parser→filter-SPOT-integration utan DB.
//
// F4-14 → F4-15 (ADR 0076 Decision 4/5/6/7) — match-sort-grenen bygger nu en FULL
// profil ur de plaintext TopSkills (BuildFullForSortAsync, SORT-vägen — ingen
// DEK) och skickar en FullCandidateMatchProfile till SearchPerUserAsync. SSYK-gaten
// läser nu profile.Fast.SsykGroupConceptIds. Default-stubben ger en HONEST EMPTY full
// profil (tom Fast-SSYK) så att alla icke-match-tester faller till search.SearchAsync.
public class ListJobAdsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly IPerUserJobAdSearchQuery _matchSearch =
        Substitute.For<IPerUserJobAdSearchQuery>();
    private readonly IMatchProfileBuilder _profileBuilder =
        Substitute.For<IMatchProfileBuilder>();
    private readonly ISearchQueryParser _parser = new SearchQueryParser();
    // #383 — db + currentUser drive the seeker-resolution branch; only touched when the
    // status filter is active (these adapter tests never activate it → unused stubs).
    private readonly IAppDbContext _db = Substitute.For<IAppDbContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public ListJobAdsQueryHandlerTests()
    {
        // Default: en honest EMPTY FULL profil (tom Fast-SSYK). Då faller även en
        // MatchDesc-begäran tillbaka till search.SearchAsync (Decision 7) — alla
        // adapter-/parser-tester nedan träffar därför den rena porten.
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyFullProfile);
    }

    private static CandidateMatchProfile EmptyFast =>
        new(Title: string.Empty, SsykGroupConceptIds: [], PreferredRegionConceptIds: [], PreferredEmploymentTypeConceptIds: [], PreferredMunicipalityConceptIds: []);

    private static FullCandidateMatchProfile EmptyFullProfile =>
        new(EmptyFast, []);

    private static FullCandidateMatchProfile FullProfileWithOccupation(
        IReadOnlyList<string>? ssyk = null,
        IReadOnlyList<string>? regions = null,
        IReadOnlyList<string>? employment = null,
        IReadOnlyList<string>? cvSkills = null) =>
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ssyk ?? ["grp-occupation"],
                PreferredRegionConceptIds: regions ?? [],
                PreferredEmploymentTypeConceptIds: employment ?? [],
                PreferredMunicipalityConceptIds: []),
            cvSkills ?? []);

    private ListJobAdsQueryHandler NewHandler() =>
        new(_search, _matchSearch, _profileBuilder, _parser, _db, _currentUser);

    private static PagedResult<JobAdDto> EmptyPage(int page = 1, int pageSize = 20) =>
        new([], 0, page, pageSize);

    // --- Adapter mapping (unchanged by F4-15) ------------------------------

    [Fact]
    public async Task Handle_WhenRegionIsNull_MapsToEmptyFilterRegionList()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(Region: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Region.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenRegionProvided_PassesListThroughToFilter()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Region: ["stockholm", "uppsala"]),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Region.ShouldBe(["stockholm", "uppsala"]);
    }

    [Fact]
    public void ListJobAdsQuery_HasNoSsykParameter_AfterC2()
    {
        typeof(ListJobAdsQuery).GetProperty("Ssyk").ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenOccupationGroupIsNull_MapsToEmptyFilterOccupationGroupList()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(OccupationGroup: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.OccupationGroup.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenMunicipalityIsNull_MapsToEmptyFilterMunicipalityList()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Municipality: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Municipality.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenOccupationGroupAndMunicipalityProvided_PassesListsThroughToFilter()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                OccupationGroup: ["grp-1", "grp-2"], Municipality: ["sthlm_kn"]),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.OccupationGroup.ShouldBe(["grp-1", "grp-2"]);
        captured.Filter.Municipality.ShouldBe(["sthlm_kn"]);
    }

    [Fact]
    public async Task Handle_MapsQToFilter_AndSortPageSizeToCriteria()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage(page: 3, pageSize: 15));
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Page: 3,
                PageSize: 15,
                Sort: ListJobAdsSort.Relevance,
                Q: "utvecklare"),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBe("utvecklare");
        captured.SortBy.ShouldBe(JobAdSortBy.Relevance);
        captured.Page.ShouldBe(3);
        captured.PageSize.ShouldBe(15);
    }

    [Fact]
    public async Task Handle_WithDefaultQuery_MapsDefaultsToCriteria()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Page.ShouldBe(1);
        captured.PageSize.ShouldBe(20);
        captured.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        captured.Filter.Q.ShouldBeNull();
        captured.Filter.OccupationGroup.ShouldBeEmpty();
        captured.Filter.Municipality.ShouldBeEmpty();
        captured.Filter.Region.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsPortResultUnchanged()
    {
        var dto = new JobAdDto(
            Guid.NewGuid(), "Backend-utvecklare", "Klarna", "Beskrivning",
            "https://example.com/1", "Manual", "Active",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow);
        var portResult = new PagedResult<JobAdDto>([dto], totalCount: 1, page: 1, pageSize: 20);
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(portResult);
        var handler = NewHandler();

        var result = await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(portResult);
        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public async Task Handle_DelegatesToPortExactlyOnce()
    {
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    // --- Fas D2 (ADR 0067 5c): parser-inkoppling ----------------------------

    [Fact]
    public async Task Handle_QWithSurroundingWhitespace_NormalizesBeforeFilter()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Q: "  utvecklare  "), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBe("utvecklare");
    }

    [Fact]
    public async Task Handle_QWithInternalWhitespaceRun_CollapsesBeforeFilter()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Q: "system   utvecklare"), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBe("system utvecklare");
    }

    [Fact]
    public async Task Handle_QIsNull_StaysNullAfterParser()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(Q: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_QNormalizesToSubMinLength_BecomesNull()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(Q: " a "), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_DimensionsUnaffectedByParser_PassThroughUnchanged()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Q: "  utvecklare  ",
                OccupationGroup: ["grp-1", "grp-2"],
                Municipality: ["sthlm_kn"],
                Region: ["stockholm"]),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBe("utvecklare");
        captured.Filter.OccupationGroup.ShouldBe(["grp-1", "grp-2"]);
        captured.Filter.Municipality.ShouldBe(["sthlm_kn"]);
        captured.Filter.Region.ShouldBe(["stockholm"]);
    }

    // --- F4-14 → F4-15 (ADR 0076 Decision 4/5/6/7): match-sort-grenen -------
    // SortByMatch + FULL-profil med angiven Fast-yrkesgrupp → matchSearch.SearchPerUserAsync
    //   (med en FullCandidateMatchProfile). Profilen byggs ur BuildFullForSortAsync
    //   (SORT-vägen, ingen DEK). SortByMatch + tom Fast-SSYK → honest fallback till
    //   search.SearchAsync. Sort != MatchDesc → alltid search.SearchAsync, profilen byggs ALDRIG.

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_CallsMatchSearchExactlyOnce_NotDefaultSearch()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        await _matchSearch.Received(1).SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_PassesFilterPageAndBuiltFullProfile()
    {
        var builtProfile = FullProfileWithOccupation(
            ssyk: ["grp-occupation"], regions: ["stockholm"], employment: ["et_fast"],
            cvSkills: ["skill-csharp"]);
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(builtProfile);

        JobAdFilterCriteria? capturedFilter = null;
        FullCandidateMatchProfile? capturedProfile = null;
        var capturedPage = 0;
        var capturedPageSize = 0;
        _matchSearch.SearchPerUserAsync(
            Arg.Do<JobAdFilterCriteria>(f => capturedFilter = f),
            Arg.Do<FullCandidateMatchProfile>(p => capturedProfile = p),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Do<int>(p => capturedPage = p),
            Arg.Do<int>(ps => capturedPageSize = ps),
            Arg.Any<CancellationToken>())
            .Returns(EmptyPage(page: 2, pageSize: 10));
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Page: 2,
                PageSize: 10,
                Sort: ListJobAdsSort.MatchDesc,
                OccupationGroup: ["grp-1"],
                Region: ["stockholm"],
                Q: "  utvecklare  "),
            TestContext.Current.CancellationToken);

        capturedFilter.ShouldNotBeNull();
        capturedFilter!.OccupationGroup.ShouldBe(["grp-1"]);
        capturedFilter.Region.ShouldBe(["stockholm"]);
        capturedFilter.Q.ShouldBe("utvecklare"); // parsad EN gång, samma som default-vägen
        capturedProfile.ShouldBeSameAs(builtProfile);
        capturedPage.ShouldBe(2);
        capturedPageSize.ShouldBe(10);
    }

    [Fact]
    public async Task Handle_SortByMatchWithEmptyOccupation_FallsBackToDefaultSearch_WithPublishedAtDesc()
    {
        // Decision 7 honest fallback: gaten läser profile.Fast.SsykGroupConceptIds.
        // Tom Fast-SSYK → faller tillbaka till default-sorten (PublishedAtDesc).
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyFullProfile); // tom Fast-SSYK
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortNotMatchDesc_NeverConsultsProfileBuilderOrMatchSearch()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtAsc),
            TestContext.Current.CancellationToken);

        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_BuildsFullProfileAtMostOnce_ViaTopSkillsPath()
    {
        // SORT-vägen bygger profilen via BuildFullForSortAsync (ingen DEK) — EN gång.
        // BuildFullForVerdictAsync (TAG-vägen) anropas ALDRIG av list-sorten.
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        await _profileBuilder.Received(1).BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _profileBuilder.DidNotReceive().BuildFullForVerdictAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_ReturnsMatchPortResultUnchanged()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        var dto = new JobAdDto(
            Guid.NewGuid(), "Backend-utvecklare", "Klarna", "Beskrivning",
            "https://example.com/1", "Manual", "Active",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow);
        var matchResult = new PagedResult<JobAdDto>([dto], totalCount: 1, page: 1, pageSize: 20);
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(matchResult);
        var handler = NewHandler();

        var result = await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(matchResult);
    }

    // --- ADR 0079 STEG 5: grad-filter × sort frikopplade ---------------------
    // MatchContextActive (icke-tom MatchGrades) driver per-användar-vägen OBEROENDE
    // av sort. Grad + ren sort → orderByMatchRank=false (delad ApplySort över den
    // grad-filtrerade mängden). Grad + MatchDesc → orderByMatchRank=true (match-rank).
    // Grad + tom SSYK → honest anon-fallback (case 2). Tom grad + ren sort = av.

    [Fact]
    public async Task Handle_MatchGradesWithOccupation_NonMatchSort_CallsPerUserSearch_DecoupledFromSort()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        IReadOnlyList<MatchGrade>? capturedGrades = null;
        var capturedSort = JobAdSortBy.Relevance;
        var capturedOrderByRank = true;
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Do<IReadOnlyList<MatchGrade>>(g => capturedGrades = g),
            Arg.Do<JobAdSortBy>(s => capturedSort = s),
            Arg.Do<bool>(o => capturedOrderByRank = o),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Sort: ListJobAdsSort.PublishedAtDesc,
                MatchGrades: [MatchGrade.Good, MatchGrade.Strong]),
            TestContext.Current.CancellationToken);

        await _matchSearch.Received(1).SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        capturedGrades.ShouldBe([MatchGrade.Good, MatchGrade.Strong]);
        // Frikopplat: graden tvingar INTE match-rank — den valda rena sorten bärs vidare.
        capturedSort.ShouldBe(JobAdSortBy.PublishedAtDesc);
        capturedOrderByRank.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_MatchGradesWithMatchDescSort_PassesOrderByMatchRankTrue()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        var capturedOrderByRank = false;
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(),
            Arg.Do<bool>(o => capturedOrderByRank = o),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc, MatchGrades: [MatchGrade.Strong]),
            TestContext.Current.CancellationToken);

        capturedOrderByRank.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_MatchGradesWithEmptyOccupation_FallsBackToDefaultSearch_NeverPerUserSearch()
    {
        // CTO-re-bind case 2: grad-filter utan angiven yrkesgrupp kan inte beräkna en
        // grad → honest anon-fallback med den valda sorten, ALDRIG en tom grad-filtrerad
        // sida (FE döljer kontrollen då; detta är server-grinden bakom).
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyFullProfile);
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtDesc, MatchGrades: [MatchGrade.Strong]),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyMatchGrades_NonMatchSort_TreatedAsMatchingOff_AnonPath()
    {
        // Klas-val: av = noll grader. Tom MatchGrades + icke-match-sort → varken
        // MatchContextActive eller SortByMatch → ren anon-väg, profilen byggs aldrig.
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtDesc, MatchGrades: []),
            TestContext.Current.CancellationToken);

        await _profileBuilder.DidNotReceive().BuildFullForSortAsync(Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    // --- #419 pt1 (CTO Approach A): "Visa bara matchade" (OnlyMatched) ----------------------
    // OnlyMatched enters the per-user path and — when NO explicit grade subset is set —
    // injects the full filterable Fast band (Basic/Related/Good/Strong = all positive ranks),
    // which the existing positive-only grade-WHERE turns into "exclude untagged (rank 0)".
    // A specific grade subset WINS (OnlyMatched is the empty-subset case). No occupation →
    // honest anon fallback (the FE hides the control then).

    [Fact]
    public async Task Handle_OnlyMatchedWithOccupation_EmptyGrades_InjectsAllFilterableGrades()
    {
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        IReadOnlyList<MatchGrade>? capturedGrades = null;
        var capturedOrderByRank = true;
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Do<IReadOnlyList<MatchGrade>>(g => capturedGrades = g),
            Arg.Any<JobAdSortBy>(), Arg.Do<bool>(o => capturedOrderByRank = o),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtDesc, OnlyMatched: true),
            TestContext.Current.CancellationToken);

        // The per-user path is taken (NOT the anon path) and the injected grade-set is the
        // full filterable Fast band → the grade-WHERE excludes only the untagged (rank 0).
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        capturedGrades.ShouldBe(
            [MatchGrade.Basic, MatchGrade.Related, MatchGrade.Good, MatchGrade.Strong]);
        // OnlyMatched alone never forces match-rank order — the chosen plain sort is kept.
        capturedOrderByRank.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_OnlyMatchedWithExplicitGradeSubset_SubsetWins_NotInjected()
    {
        // A specific grade subset is already a stricter "only matched" (every selected rank is
        // positive) → the subset is the binding filter; OnlyMatched adds nothing (no injection).
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(FullProfileWithOccupation());
        IReadOnlyList<MatchGrade>? capturedGrades = null;
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Do<IReadOnlyList<MatchGrade>>(g => capturedGrades = g),
            Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Sort: ListJobAdsSort.PublishedAtDesc,
                MatchGrades: [MatchGrade.Good],
                OnlyMatched: true),
            TestContext.Current.CancellationToken);

        capturedGrades.ShouldBe([MatchGrade.Good]);
    }

    [Fact]
    public async Task Handle_OnlyMatchedWithEmptyOccupation_FallsBackToDefaultSearch_NeverPerUserSearch()
    {
        // No stated occupation → no grade is computable → honest anon fallback (never an empty
        // only-matched page). Parity with the grade-filter case-2 gate; FE hides the control.
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyFullProfile);
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtDesc, OnlyMatched: true),
            TestContext.Current.CancellationToken);

        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // --- #300 PR-5a: ListJobAdsQuery.IncludeRelated threads into BuildFullForSortAsync ----
    // The per-user path builds the FULL profile via BuildFullForSortAsync; PR-5a adds the
    // includeRelated arg that the FE toggle flips (ADR 0084 question A, off by default). The
    // flag is RUNTIME-CONTEXT (like MatchGrades) — it NEVER enters ICapturesRecentSearch /
    // SearchCriteria / FilterHash. These tests stub with Arg.Any<bool>() and capture the
    // threaded arg via Arg.Do<bool>. RED until ListJobAdsQuery carries IncludeRelated AND the
    // handler passes includeRelated: query.IncludeRelated into BuildFullForSortAsync (per-user
    // branch).

    [Fact]
    public async Task Handle_MatchContextActive_ThreadsIncludeRelatedTrue_ToBuildFullForSort()
    {
        var capturedIncludeRelated = false;
        _profileBuilder.BuildFullForSortAsync(
                Arg.Any<CancellationToken>(), Arg.Do<bool>(r => capturedIncludeRelated = r))
            .Returns(FullProfileWithOccupation());
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Sort: ListJobAdsSort.PublishedAtDesc,
                MatchGrades: [MatchGrade.Related, MatchGrade.Good],
                IncludeRelated: true),
            TestContext.Current.CancellationToken);

        capturedIncludeRelated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_SortByMatch_ThreadsIncludeRelatedFalse_ToBuildFullForSort_WhenFalse()
    {
        var capturedIncludeRelated = true;
        _profileBuilder.BuildFullForSortAsync(
                Arg.Any<CancellationToken>(), Arg.Do<bool>(r => capturedIncludeRelated = r))
            .Returns(FullProfileWithOccupation());
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc, IncludeRelated: false),
            TestContext.Current.CancellationToken);

        capturedIncludeRelated.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_PerUserPath_DefaultsIncludeRelatedToFalse_WhenOmitted()
    {
        // ADR 0084 question A — off by default. A per-user request that omits IncludeRelated
        // (today's only production path until the PR-5 FE toggle) builds the exact-only profile.
        var capturedIncludeRelated = true;
        _profileBuilder.BuildFullForSortAsync(
                Arg.Any<CancellationToken>(), Arg.Do<bool>(r => capturedIncludeRelated = r))
            .Returns(FullProfileWithOccupation());
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        capturedIncludeRelated.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_IncludeRelatedTrue_DoesNotPolluteFilterIdentity_StaysRuntimeContext()
    {
        // ADR 0079 STEG 5 isolation parity (MatchGrades): IncludeRelated is runtime-context —
        // it must NEVER bleed into the JobAdFilterCriteria the per-user path composes (the
        // anonymous search identity / FilterHash reads only the named filter fields). The filter
        // for IncludeRelated true must equal the filter for the named dimensions alone.
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(FullProfileWithOccupation());
        JobAdFilterCriteria? capturedFilter = null;
        _matchSearch.SearchPerUserAsync(
            Arg.Do<JobAdFilterCriteria>(f => capturedFilter = f),
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Sort: ListJobAdsSort.MatchDesc,
                OccupationGroup: ["grp-1"],
                Region: ["stockholm"],
                MatchGrades: [MatchGrade.Related],
                IncludeRelated: true),
            TestContext.Current.CancellationToken);

        // The filter carries ONLY the named dimensions — no IncludeRelated leakage into it.
        capturedFilter.ShouldNotBeNull();
        capturedFilter!.OccupationGroup.ShouldBe(["grp-1"]);
        capturedFilter.Region.ShouldBe(["stockholm"]);
    }

    [Fact]
    public void ListJobAdsQuery_IncludeRelated_IsNotPersistedSearchIdentity()
    {
        // Defence-in-depth: IncludeRelated is NOT part of ICapturesRecentSearch (the captured
        // anonymous search identity). The capture interface exposes only the named filter
        // fields + Commit/Since-style runtime context, never IncludeRelated.
        typeof(Jobbliggaren.Application.RecentJobSearches.Common.ICapturesRecentSearch)
            .GetProperty("IncludeRelated").ShouldBeNull(
                "IncludeRelated är runtime-kontext (analogt MatchGrades) och får aldrig " +
                "ingå i ICapturesRecentSearch / sök-identiteten (ADR 0084 / ADR 0079 STEG 5).");
    }

    // --- #383 (CTO-bind cto-7f3a9c2e1b4d8a6f): status-filter-grenen ----------
    // Status (SavedOnly/AppliedOnly/HideApplied) driver seeker-resolution + en av två
    // status-vägar, FRIKOPPLAT från match-gaten:
    //   * status INAKTIVT → byte-for-byte som förr (anon search.SearchAsync, eller match-
    //     vägen med JobAdStatusFilter.None) — _db rörs ALDRIG.
    //   * status AKTIVT, ingen SSYK → SearchByStatusAsync (status-only, ingen profil/grad).
    //   * status AKTIVT, SSYK + MatchDesc → SearchPerUserAsync (status komponeras IN i match).
    //   * status AKTIVT, ingen seeker → tom sida (defense-in-depth-backstopp).
    //
    // Seeker-resolution är en trivial equality+projektion (db.JobSeekers.Where(UserId ==
    // ...).Select(Id).FirstOrDefaultAsync) — provider-agnostisk, INGEN Postgres-/VO-kolumn-
    // översättning (den hårda VO==VO-EXISTS:en bor i Infrastructure-query:n och pinnas av
    // Testcontainers-oraklet). Den NSubstitute-stubbade IAppDbContext implementerar inte
    // IAsyncEnumerable → kan inte köra den async-LINQ:n; vi använder därför en RIKTIG
    // InMemory-AppDbContext (TestAppDbContextFactory) ENBART för seeker-lookupen, med kvar
    // de mockade portarna för router-assertions. (CV-/jsonb-/constraint-fällorna som gör
    // InMemory olämplig finns inte i denna lookup.)

    private static readonly DateTimeOffset SeedClockInstant =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static IDateTimeProvider SeedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(SeedClockInstant);
        return clock;
    }

    private ListJobAdsQueryHandler NewHandlerWith(IAppDbContext db) =>
        new(_search, _matchSearch, _profileBuilder, _parser, db, _currentUser);

    // Seeds a JobSeeker for userId into a fresh InMemory context and returns it + the seeker id.
    private static async Task<(AppDbContext Db, JobSeekerId SeekerId)> SeededDbAsync(
        Guid userId, CancellationToken ct)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(userId, "Status User", SeedClock()).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return (db, seeker.Id);
    }

    [Fact]
    public async Task Handle_StatusInactive_RoutesToAnonSearch_AndNeverTouchesDb()
    {
        // Default query (status av) → den anonyma sök-vägen, oförändrad. _db (mocken) rörs
        // aldrig — seeker-resolution sker bara när status är aktivt.
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        // Status inaktiv → ingen seeker-lookup på den mockade IAppDbContext.
        _ = _db.DidNotReceive().JobSeekers;
    }

    [Fact]
    public async Task Handle_MatchPathWithStatusInactive_PassesJobAdStatusFilterNone()
    {
        // Match-vägen (SSYK + MatchDesc) utan status → SearchPerUserAsync får
        // JobAdStatusFilter.None (no-op), byte-for-byte som före #383.
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(FullProfileWithOccupation());
        var capturedStatus = new JobAdStatusFilter(true, true, true);
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Do<JobAdStatusFilter>(s => capturedStatus = s), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        capturedStatus.ShouldBe(JobAdStatusFilter.None);
        capturedStatus.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_StatusActive_NoOccupation_RoutesToSearchByStatus_WithResolvedSeekerAndStatus()
    {
        // Status aktivt + ingen SSYK → den frikopplade status-only-vägen (SearchByStatusAsync),
        // ALDRIG SearchPerUserAsync eller anon-search. Den resolverade seekern + status-filtret
        // bärs vidare.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, seekerId) = await SeededDbAsync(userId, ct);
        using var _ = db;
        _currentUser.UserId.Returns(userId);

        JobAdStatusFilter capturedStatus = default;
        JobSeekerId capturedSeeker = default;
        _matchSearch.SearchByStatusAsync(
            Arg.Any<JobAdFilterCriteria>(),
            Arg.Do<JobSeekerId>(s => capturedSeeker = s),
            Arg.Do<JobAdStatusFilter>(s => capturedStatus = s),
            Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandlerWith(db);

        await handler.Handle(new ListJobAdsQuery(SavedOnly: true), ct);

        await _matchSearch.Received(1).SearchByStatusAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<JobSeekerId>(), Arg.Any<JobAdStatusFilter>(),
            Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        capturedSeeker.ShouldBe(seekerId);
        capturedStatus.ShouldBe(new JobAdStatusFilter(SavedOnly: true, AppliedOnly: false, HideApplied: false));
    }

    [Fact]
    public async Task Handle_StatusActive_WithOccupationAndMatchSort_RoutesToPerUserSearch_WithStatusAndSeeker()
    {
        // Status aktivt + SSYK + MatchDesc → match-vägen, med status komponerat IN i
        // SearchPerUserAsync (samma grad-rank, plus status-EXISTS:en) och den resolverade seekern.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var (db, seekerId) = await SeededDbAsync(userId, ct);
        using var _ = db;
        _currentUser.UserId.Returns(userId);
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(FullProfileWithOccupation());

        JobAdStatusFilter capturedStatus = default;
        JobSeekerId capturedSeeker = default;
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Do<JobAdStatusFilter>(s => capturedStatus = s),
            Arg.Do<JobSeekerId>(s => capturedSeeker = s),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandlerWith(db);

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc, SavedOnly: true, HideApplied: true),
            ct);

        await _matchSearch.Received(1).SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchByStatusAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<JobSeekerId>(), Arg.Any<JobAdStatusFilter>(),
            Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        capturedSeeker.ShouldBe(seekerId);
        capturedStatus.ShouldBe(new JobAdStatusFilter(SavedOnly: true, AppliedOnly: false, HideApplied: true));
    }

    [Fact]
    public async Task Handle_StatusActive_NoSeekerResolves_ReturnsEmptyPage_AndCallsNoPort()
    {
        // Status aktivt men ingen JobSeeker-rad för UserId (eller anonym) → tom sida,
        // ingen port anropas alls (defense-in-depth-backstoppen; FE döljer kontrollen).
        var ct = TestContext.Current.CancellationToken;
        using var db = TestAppDbContextFactory.Create(); // tom — ingen seeker
        _currentUser.UserId.Returns(Guid.NewGuid());
        var handler = NewHandlerWith(db);

        var result = await handler.Handle(
            new ListJobAdsQuery(Page: 2, PageSize: 10, AppliedOnly: true), ct);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(2);
        result.PageSize.ShouldBe(10);
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchByStatusAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<JobSeekerId>(), Arg.Any<JobAdStatusFilter>(),
            Arg.Any<JobAdSortBy>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<JobAdStatusFilter>(), Arg.Any<JobSeekerId>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StatusActive_AnonymousUser_ReturnsEmptyPage_WithoutQueryingDb()
    {
        // Anonym (ingen UserId) + aktivt status → tom sida utan att ens slå mot db.JobSeekers
        // (ResolveSeekerIdAsync kortsluter på !UserId.HasValue). Mockad IAppDbContext räcker.
        var ct = TestContext.Current.CancellationToken;
        _currentUser.UserId.Returns((Guid?)null);
        var handler = NewHandler();

        var result = await handler.Handle(new ListJobAdsQuery(SavedOnly: true), ct);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        _ = _db.DidNotReceive().JobSeekers;
    }
}
