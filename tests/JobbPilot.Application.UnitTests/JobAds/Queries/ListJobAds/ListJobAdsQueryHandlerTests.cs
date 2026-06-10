using JobbPilot.Application.Common;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Internal;
using JobbPilot.Application.JobAds.Queries;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Domain.JobAds;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.ListJobAds;

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
public class ListJobAdsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly ISearchQueryParser _parser = new SearchQueryParser();

    private static PagedResult<JobAdDto> EmptyPage(int page = 1, int pageSize = 20) =>
        new([], 0, page, pageSize);

    [Fact]
    public async Task Handle_WhenRegionIsNull_MapsToEmptyFilterRegionList()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var since = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage(page: 3, pageSize: 15));
        var handler = new ListJobAdsQueryHandler(_search, _parser);

        await handler.Handle(
            new ListJobAdsQuery(
                Page: 3,
                PageSize: 15,
                SortBy: JobAdSortBy.Relevance,
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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
        var handler = new ListJobAdsQueryHandler(_search, _parser);

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
}
