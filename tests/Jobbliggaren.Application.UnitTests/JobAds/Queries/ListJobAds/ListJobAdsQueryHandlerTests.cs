using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.Matching.Abstractions;
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
// Fas D2 (ADR 0067 Beslut 5c) — ctor:n tar nu en ISearchQueryParser utöver
// porten. Vi injicerar en RIKTIG SearchQueryParser (ren CPU, deterministisk,
// InternalsVisibleTo) snarare än en mock: det ger äkta integration av
// parser→filter-SPOT:en utan DB. Parsern är idempotent på redan-rena värden
// ("utvecklare" → "utvecklare") så befintliga assertions står kvar.
//
// F4-14 (ADR 0076 Decision 4/5/7) — ctor:n tar nu OCKSÅ den per-användar-match-
// sort-porten (IMatchSortedJobAdSearchQuery) + IMatchProfileBuilder. Default-
// vägen (icke-match-sort, ELLER match-sort med tom yrkesgrupp-gate) går
// fortfarande genom IJobAdSearchQuery.SearchAsync med ett rent SortBy-värde.
// matchSearch + profileBuilder mockas; profileBuilder ger som default en HONEST
// EMPTY profil (tom SSYK) så att alla icke-match-tester träffar search.SearchAsync.
public class ListJobAdsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly IMatchSortedJobAdSearchQuery _matchSearch =
        Substitute.For<IMatchSortedJobAdSearchQuery>();
    private readonly IMatchProfileBuilder _profileBuilder =
        Substitute.For<IMatchProfileBuilder>();
    private readonly ISearchQueryParser _parser = new SearchQueryParser();

    public ListJobAdsQueryHandlerTests()
    {
        // Default: en honest EMPTY profil (tom SSYK-lista). Då faller även en
        // MatchDesc-begäran tillbaka till search.SearchAsync (Decision 7) —
        // alla adapter-/parser-tester nedan träffar därför den rena porten.
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyProfile);
    }

    private static CandidateMatchProfile EmptyProfile =>
        new(Title: string.Empty, SsykGroupConceptIds: [], PreferredRegionConceptIds: [], PreferredEmploymentTypeConceptIds: []);

    private static CandidateMatchProfile ProfileWithOccupation(
        IReadOnlyList<string>? ssyk = null,
        IReadOnlyList<string>? regions = null,
        IReadOnlyList<string>? employment = null) =>
        new(
            Title: string.Empty,
            SsykGroupConceptIds: ssyk ?? ["grp-occupation"],
            PreferredRegionConceptIds: regions ?? [],
            PreferredEmploymentTypeConceptIds: employment ?? []);

    private ListJobAdsQueryHandler NewHandler() =>
        new(_search, _matchSearch, _profileBuilder, _parser);

    private static PagedResult<JobAdDto> EmptyPage(int page = 1, int pageSize = 20) =>
        new([], 0, page, pageSize);

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
        // C2 (CTO-dom (e)): Ssyk-paramen är borttagen — fältet var en lögn i
        // kontraktet efter att equality-grenen togs i C1 (no-op-param).
        typeof(ListJobAdsQuery).GetProperty("Ssyk").ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenOccupationGroupIsNull_MapsToEmptyFilterOccupationGroupList()
    {
        // C1 (ADR 0067) — ny dimension: null → tom lista innan porten anropas
        // (samma normalisering som Ssyk/Region).
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
        // Q hör hemma i Filter-SPOT:en; SortBy/Page/PageSize/Since på
        // JobAdSearchCriteria. Verifierar att varje fält hamnar på rätt plats.
        // F4-14: Sort=Relevance mappas till JobAdSortBy.Relevance via ToDomainSort.
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
        // Adaptern returnerar port-resultatet rakt — ingen efterbearbetning.
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

    // --- Fas D2 (ADR 0067 5c): parser-inkoppling låses här ------------------
    // Q normaliseras av ISearchQueryParser INNAN den landar på filter-SPOT:en.
    // Dimensioner (OccupationGroup/Municipality/Region) rörs INTE av parsern.

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
        // Recall-bevarande: residual under QMinLength → Q=null; dimensioner orörda.
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
        // Parsern rör BARA Q. OccupationGroup/Municipality/Region passerar rakt
        // igenom (parsern trimmar/normaliserar inte dimensions-concept-ids).
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

    // --- F4-14 (ADR 0076 Decision 4/5/7): match-sort-grenen ------------------
    // SortByMatch + profil med angiven yrkesgrupp → matchSearch.SearchByMatchAsync.
    // SortByMatch + tom yrkesgrupp → honest fallback till search.SearchAsync.
    // Sort != MatchDesc → alltid search.SearchAsync.

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_CallsMatchSearchExactlyOnce_NotDefaultSearch()
    {
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        _matchSearch.SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        await _matchSearch.Received(1).SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _search.DidNotReceive().SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_PassesFilterPageSinceAndBuiltProfile()
    {
        // Match-grenen ska ärva EXAKT samma filter (incl. parsad Q) + page/pageSize/
        // since som default-vägen, och den byggda profilen oförändrad.
        var since = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var builtProfile = ProfileWithOccupation(
            ssyk: ["grp-occupation"], regions: ["stockholm"], employment: ["et_fast"]);
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(builtProfile);

        JobAdFilterCriteria? capturedFilter = null;
        CandidateMatchProfile? capturedProfile = null;
        var capturedPage = 0;
        var capturedPageSize = 0;
        DateTimeOffset? capturedSince = null;
        _matchSearch.SearchByMatchAsync(
            Arg.Do<JobAdFilterCriteria>(f => capturedFilter = f),
            Arg.Do<CandidateMatchProfile>(p => capturedProfile = p),
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
        // Decision 7 honest fallback: tom SSYK-gate → ingen annons kan få en grad →
        // match-ordning vore meningslös → faller tillbaka till den rena default-sorten
        // (PublishedAtDesc), aldrig en fejkad ordning. matchSearch anropas ALDRIG.
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(EmptyProfile); // tom SSYK
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        await _matchSearch.DidNotReceive().SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortNotMatchDesc_NeverConsultsProfileBuilderOrMatchSearch()
    {
        // Icke-match-sort: profilen byggs ALDRIG (ingen onödig DB-läsning), och
        // matchSearch anropas aldrig — oavsett att en profil med yrkesgrupp finns.
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        _search.SearchAsync(Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.PublishedAtAsc),
            TestContext.Current.CancellationToken);

        await _profileBuilder.DidNotReceive().BuildFromPreferencesAsync(Arg.Any<CancellationToken>());
        await _matchSearch.DidNotReceive().SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_BuildsProfileAtMostOnce()
    {
        // Profilen byggs EN gång på match-grenen (ingen dubbel-läsning av preferenser).
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        _matchSearch.SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = NewHandler();

        await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        await _profileBuilder.Received(1).BuildFromPreferencesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SortByMatchWithOccupation_ReturnsMatchPortResultUnchanged()
    {
        _profileBuilder.BuildFromPreferencesAsync(Arg.Any<CancellationToken>())
            .Returns(ProfileWithOccupation());
        var dto = new JobAdDto(
            Guid.NewGuid(), "Backend-utvecklare", "Klarna", "Beskrivning",
            "https://example.com/1", "Manual", "Active",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, IsNew: false);
        var matchResult = new PagedResult<JobAdDto>([dto], totalCount: 1, page: 1, pageSize: 20);
        _matchSearch.SearchByMatchAsync(
            Arg.Any<JobAdFilterCriteria>(), Arg.Any<CandidateMatchProfile>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(matchResult);
        var handler = NewHandler();

        var result = await handler.Handle(
            new ListJobAdsQuery(Sort: ListJobAdsSort.MatchDesc),
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(matchResult);
    }
}
