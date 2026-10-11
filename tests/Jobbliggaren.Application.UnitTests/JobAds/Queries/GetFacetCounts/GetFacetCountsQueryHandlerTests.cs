using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetFacetCounts;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetFacetCounts;

// Fas E2c (ADR 0067 Beslut 4) — GetFacetCountsQueryHandler är en TUNN ADAPTER
// (spegelbild av ListJobAdsQueryHandler): mappar query → JobAdFilterCriteria och
// delegerar till IJobAdSearchQuery.FacetCountsAsync med rå Q — porten parsar den för
// varje konsument (#2143, residual-konsistens med list-vägen — E2c-architect §2).
// GROUP BY-kompositionen testas mot riktig Postgres i JobAdFacetCountsTests.
public class GetFacetCountsQueryHandlerTests
{
    private readonly IJobAdSearchQuery _search = Substitute.For<IJobAdSearchQuery>();

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
        var handler = new GetFacetCountsQueryHandler(_search);

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
        var handler = new GetFacetCountsQueryHandler(_search);

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

    // #551 PR-B D5/D7 — an active Distans selection must reach the port as filter context, else a
    // facet-count for another dimension counts against a different WHERE than the list (residual
    // inconsistency). The port's ExcludeDimension then decides whether to keep or drop it per facet.
    [Fact]
    public async Task Handle_ThreadsRemoteFilterContextToPort()
    {
        JobAdFilterCriteria? captured = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search);

        await handler.Handle(
            new GetFacetCountsQuery(FacetDimension.OccupationGroup, Remote: true),
            TestContext.Current.CancellationToken);

        captured!.Remote.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_PassesTheRawQToThePort()
    {
        // #2143 — the port parses Q, minus-words included, for every consumer; a handler that
        // parsed first would drop the excluded terms before they reached it.
        JobAdFilterCriteria? captured = null;
        _search.FacetCountsAsync(
                Arg.Do<JobAdFilterCriteria>(c => captured = c),
                Arg.Any<FacetDimension>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyCounts());
        var handler = new GetFacetCountsQueryHandler(_search);

        await handler.Handle(
            new GetFacetCountsQuery(
                FacetDimension.Municipality,
                Q: "  lärare   -deltid  "),
            TestContext.Current.CancellationToken);

        captured!.Q.ShouldBe("  lärare   -deltid  ");
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
        var handler = new GetFacetCountsQueryHandler(_search);

        var result = await handler.Handle(
            new GetFacetCountsQuery(FacetDimension.OccupationGroup),
            TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(counts);
    }
}
