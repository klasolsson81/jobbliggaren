using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.SearchOccupations;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.SearchOccupations;

// #2198 (ADR 0180) — the handler is a thin call to the searcher port: it passes the query on and
// returns the searcher's answer untouched (the searcher is the only ordering authority).
public class SearchOccupationsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ValidQuery_ReturnsTheSearchersAnswerUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var answer = new OccupationSearchResult(
            [OccupationSearchRow.ForGroup("DJh5_yyF_hEM", "Mjukvaru- och systemutvecklare m.fl.", ["Backend-utvecklare"], false, false)],
            TotalRows: 1);
        var searcher = new FakeSearcher(answer);
        var sut = new SearchOccupationsQueryHandler(searcher);

        var result = await sut.Handle(new SearchOccupationsQuery("backend"), ct);

        result.ShouldBeSameAs(answer);
        searcher.Queries.ShouldBe(["backend"]);
    }

    [Fact]
    public void Query_IsAuthenticated_SoTheBucketAlwaysHasAPartition()
    {
        // An anonymous caller gets the rate limiter's NoLimiter partition, so the bulkhead only
        // protects while authentication holds; the pipeline enforces it independently of routing.
        new SearchOccupationsQuery("ux").ShouldBeAssignableTo<IAuthenticatedRequest>();
    }

    // Hand-rolled (not NSubstitute): a mock replaying one ValueTask trips CA2012.
    private sealed class FakeSearcher(OccupationSearchResult answer) : IOccupationSearcher
    {
        public List<string> Queries { get; } = [];

        public ValueTask<OccupationSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return new ValueTask<OccupationSearchResult>(answer);
        }
    }
}
