using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.JobAds;
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
        new(_search, _matchSearch, _profileBuilder, _parser);

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
    public async Task Handle_MapsQToFilter_AndSortPageSizeSinceToCriteria()
    {
        var since = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage(page: 3, pageSize: 15));
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(
                Page: 3,
                PageSize: 15,
                Sort: ListJobAdsSort.Relevance,
                Q: "utvecklare",
                Since: since),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Q.ShouldBe("utvecklare");
        captured.SortBy.ShouldBe(JobAdSortBy.Relevance);
        captured.Page.ShouldBe(3);
        captured.PageSize.ShouldBe(15);
        captured.Since.ShouldBe(since);
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
        captured.Since.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ReturnsPortResultUnchanged()
    {
        var dto = new JobAdDto(
            Guid.NewGuid(), "Backend-utvecklare", "Klarna", "Beskrivning",
            "https://example.com/1", "Manual", "Active",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, IsNew: true);
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        await _matchSearch.Received(1).SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_PassesFilterPageSinceAndBuiltFullProfile()
    {
        var since = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var builtProfile = FullProfileWithOccupation(
            ssyk: ["grp-occupation"], regions: ["stockholm"], employment: ["et_fast"],
            cvSkills: ["skill-csharp"]);
        _profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(builtProfile);

        JobAdFilterCriteria? capturedFilter = null;
        FullCandidateMatchProfile? capturedProfile = null;
        var capturedPage = 0;
        var capturedPageSize = 0;
        DateTimeOffset? capturedSince = null;
        _matchSearch.SearchPerUserAsync(
            Arg.Do<JobAdFilterCriteria>(f => capturedFilter = f),
            Arg.Do<FullCandidateMatchProfile>(p => capturedProfile = p),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Do<int>(p => capturedPage = p),
            Arg.Do<int>(ps => capturedPageSize = ps),
            Arg.Do<DateTimeOffset?>(s => capturedSince = s),
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
                Q: "  utvecklare  ",
                Since: since),
            TestContext.Current.CancellationToken);

        capturedFilter.ShouldNotBeNull();
        capturedFilter!.OccupationGroup.ShouldBe(["grp-1"]);
        capturedFilter.Region.ShouldBe(["stockholm"]);
        capturedFilter.Q.ShouldBe("utvecklare"); // parsad EN gång, samma som default-vägen
        capturedProfile.ShouldBeSameAs(builtProfile);
        capturedPage.ShouldBe(2);
        capturedPageSize.ShouldBe(10);
        capturedSince.ShouldBe(since);
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
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
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, IsNew: false);
        var matchResult = new PagedResult<JobAdDto>([dto], totalCount: 1, page: 1, pageSize: 20);
        _matchSearch.SearchPerUserAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<FullCandidateMatchProfile>(),
            Arg.Any<IReadOnlyList<MatchGrade>>(), Arg.Any<JobAdSortBy>(), Arg.Any<bool>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
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
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
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
}
