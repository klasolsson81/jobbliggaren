using JobbPilot.Application.Common;
using JobbPilot.Application.JobAds.Abstractions;
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
public class ListJobAdsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();

    private static PagedResult<JobAdDto> EmptyPage(int page = 1, int pageSize = 20) =>
        new([], 0, page, pageSize);

    [Fact]
    public async Task Handle_WhenSsykIsNull_MapsToEmptyFilterSsykList()
    {
        // ADR 0042 Beslut B — null betyder "inget filter" → handlern normaliserar
        // null → tom lista innan porten anropas.
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = new ListJobAdsQueryHandler(_search);

        await handler.Handle(new ListJobAdsQuery(Ssyk: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Ssyk.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenRegionIsNull_MapsToEmptyFilterRegionList()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = new ListJobAdsQueryHandler(_search);

        await handler.Handle(new ListJobAdsQuery(Region: null), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Region.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSsykAndRegionProvided_PassesListsThroughToFilter()
    {
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = new ListJobAdsQueryHandler(_search);

        await handler.Handle(
            new ListJobAdsQuery(Ssyk: ["1234", "5678"], Region: ["stockholm"]),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Filter.Ssyk.ShouldBe(["1234", "5678"]);
        captured.Filter.Region.ShouldBe(["stockholm"]);
    }

    [Fact]
    public async Task Handle_WhenOccupationGroupIsNull_MapsToEmptyFilterOccupationGroupList()
    {
        // C1 (ADR 0067) — ny dimension: null → tom lista innan porten anropas
        // (samma normalisering som Ssyk/Region).
        JobAdSearchCriteria? captured = null;
        _search.SearchAsync(Arg.Do<JobAdSearchCriteria>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(EmptyPage());
        var handler = new ListJobAdsQueryHandler(_search);

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
        var handler = new ListJobAdsQueryHandler(_search);

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
        var handler = new ListJobAdsQueryHandler(_search);

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
        var handler = new ListJobAdsQueryHandler(_search);

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
        var handler = new ListJobAdsQueryHandler(_search);

        await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.Page.ShouldBe(1);
        captured.PageSize.ShouldBe(20);
        captured.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
        captured.Filter.Q.ShouldBeNull();
        captured.Filter.OccupationGroup.ShouldBeEmpty();
        captured.Filter.Municipality.ShouldBeEmpty();
        captured.Filter.Ssyk.ShouldBeEmpty();
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
        var handler = new ListJobAdsQueryHandler(_search);

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
        var handler = new ListJobAdsQueryHandler(_search);

        await handler.Handle(new ListJobAdsQuery(), TestContext.Current.CancellationToken);

        await _search.Received(1).SearchAsync(
            Arg.Any<JobAdSearchCriteria>(), Arg.Any<CancellationToken>());
    }
}
