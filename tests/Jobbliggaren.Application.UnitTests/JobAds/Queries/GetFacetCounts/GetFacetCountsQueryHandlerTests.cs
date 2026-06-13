using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries.GetFacetCounts;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetFacetCounts;

// Fas E2c (ADR 0067 Beslut 4) — GetFacetCountsQueryHandler är en TUNN ADAPTER
// (spegelbild av ListJobAdsQueryHandler): mappar query → JobAdFilterCriteria,
// kör Q genom ISearchQueryParser (residual-konsistens med list-vägen — E2c-
// architect §2) och delegerar till IJobAdSearchQuery.FacetCountsAsync.
// GROUP BY-kompositionen testas mot riktig Postgres i JobAdFacetCountsTests.
//
// Riktig SearchQueryParser injiceras (ren CPU, deterministisk,
// InternalsVisibleTo) — samma val som ListJobAdsQueryHandlerTests (D2).
public class GetFacetCountsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();
    private readonly ISearchQueryParser _parser = new SearchQueryParser();

    private static Dictionary<string, int> EmptyCounts() => [];

    [Fact]
    public async Task Handle_WhenListsAreNull_MapsToEmptyFilterLists()
    {
        JobAdFilterCriteria? captured = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search, _parser);

        await handler.Handle(
            new GetFacetCountsQuery(FacetDimension.Municipality),
            TestContext.Current.CancellationToken);

        captured.ShouldNotBeNull();
        captured!.OccupationGroup.ShouldBeEmpty();
        captured.Municipality.ShouldBeEmpty();
        captured.Region.ShouldBeEmpty();
        captured.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_PassesDimensionAndListsThroughToPort()
    {
        JobAdFilterCriteria? captured = null;
        FacetDimension? capturedDim = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Do<FacetDimension>(d => capturedDim = d),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search, _parser);

        await handler.Handle(
            new GetFacetCountsQuery(
                FacetDimension.OccupationGroup,
                OccupationGroup: ["grpA"],
                Municipality: ["knA"],
                Region: ["regA"]),
            TestContext.Current.CancellationToken);

        capturedDim.ShouldBe(FacetDimension.OccupationGroup);
        captured!.OccupationGroup.ShouldBe(["grpA"]);
        captured.Municipality.ShouldBe(["knA"]);
        captured.Region.ShouldBe(["regA"]);
    }

    [Fact]
    public async Task Handle_RunsQThroughParser_ResidualReachesFilterNotRawInput()
    {
        // Residual-konsistens (E2c-architect §2): kontrolltecken strippas och
        // whitespace kollapsar — exakt som list-vägen. Rå input får ALDRIG nå
        // filter-SPOT:en (annars räknar facetten mot annan WHERE än listan).
        JobAdFilterCriteria? captured = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search, _parser);

        await handler.Handle(
            new GetFacetCountsQuery(
                FacetDimension.Municipality,
                Q: "  lärare   stockholm  "),
            TestContext.Current.CancellationToken);

        captured!.Q.ShouldBe("lärare stockholm");
    }

    [Fact]
    public async Task Handle_SubMinLengthQ_NormalizesToNull()
    {
        // Parser-kontraktet (D2): sub-QMinLength → null (near-full-scan-skydd)
        // — samma normalisering som list-vägen.
        JobAdFilterCriteria? captured = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search, _parser);

        await handler.Handle(
            new GetFacetCountsQuery(FacetDimension.Region, Q: "a"),
            TestContext.Current.CancellationToken);

        captured!.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ReturnsPortResultUnchanged()
    {
        var counts = new Dictionary<string, int> { ["grpA"] = 12, ["grpB"] = 3 };
        _search.FacetCountsAsync(
                Arg.Any<JobAdFilterCriteria>(),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(counts);
        var handler = new GetFacetCountsQueryHandler(_search, _parser);

        var result = await handler.Handle(
            new GetFacetCountsQuery(FacetDimension.OccupationGroup),
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(counts);
    }
}
