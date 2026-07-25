using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — the "Distans (N)" facet-hint count handler. Thin adapter (parity
/// <see cref="GetMatchCountPreview.GetMatchCountPreviewQueryHandlerTests"/>): a hand-rolled
/// ValueTask fake for the <see cref="IJobAdSearchQuery"/> port (NSubstitute trips CA2012 at the
/// ValueTask call-setup) plus the real <see cref="ISearchQueryParser"/> (pure CPU, deterministic).
/// The handler's contract — the D7 count predicate:
/// <list type="bullet">
/// <item>FORCES <c>Remote=true</c> and EMPTIES the location dimension (Municipality/Region) — the
/// facet-excluded "how many remote ads match the rest" predicate; counting remote ∧ muni would be ≈0;</item>
/// <item>threads the orthogonal non-location filters (yrke/anställningsform/omfattning) and the
/// parser-normalized residual Q; counts via <see cref="IJobAdSearchQuery.CountAsync"/> and returns
/// the port's count wrapped, unchanged.</item>
/// </list>
/// </summary>
public class GetRemoteAdCountQueryHandlerTests
{
    private sealed class FakeJobAdSearchQuery(int countToReturn) : IJobAdSearchQuery
    {
        public int CountCallCount { get; private set; }
        public JobAdFilterCriteria? LastFilter { get; private set; }

        public ValueTask<int> CountAsync(
            JobAdFilterCriteria criteria, CancellationToken cancellationToken)
        {
            CountCallCount++;
            LastFilter = criteria;
            return new ValueTask<int>(countToReturn);
        }

        public ValueTask<int> CountNewSinceAsync(
            JobAdFilterCriteria criteria, DateTimeOffset since, CancellationToken cancellationToken)
            => throw new NotSupportedException("CountNewSinceAsync ska inte anropas av remote-count-handlern.");

        public ValueTask<PagedResult<JobAdDto>> SearchAsync(
            JobAdSearchCriteria criteria, CancellationToken cancellationToken)
            => throw new NotSupportedException("SearchAsync ska inte anropas av remote-count-handlern.");

        public ValueTask<IReadOnlyDictionary<string, int>> FacetCountsAsync(
            JobAdFilterCriteria criteria, FacetDimension dimension, CancellationToken cancellationToken)
            => throw new NotSupportedException("FacetCountsAsync ska inte anropas av remote-count-handlern.");
    }

    private static GetRemoteAdCountQueryHandler CreateSut(FakeJobAdSearchQuery search) =>
        new(search, new SearchQueryParser());

    [Fact]
    public async Task Handle_ForcesRemoteTrueAndExcludesLocationDimension()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 658);
        var sut = CreateSut(search);

        await sut.Handle(new GetRemoteAdCountQuery(OccupationGroup: ["grp_dev"]), ct);

        // The D7 predicate: remote=true AND {non-location filters}. Location is EXCLUDED
        // (remote ads are location-less → remote ∧ muni ≈ 0 would lie about distansjobb).
        var filter = search.LastFilter!;
        filter.Remote.ShouldBeTrue();
        filter.Municipality.ShouldBeEmpty();
        filter.Region.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ThreadsNonLocationFilters_LeavesEmployerEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 5);
        var sut = CreateSut(search);

        await sut.Handle(
            new GetRemoteAdCountQuery(
                OccupationGroup: ["grp_dev"],
                EmploymentType: ["et_fast"],
                WorktimeExtent: ["wt_full"]),
            ct);

        // The orthogonal non-location facets narrow the remote count (a remote developer role
        // is still filtered by yrke/form/omfattning); employer facet is not exposed here.
        var filter = search.LastFilter!;
        filter.OccupationGroup.ShouldBe(["grp_dev"]);
        filter.EmploymentType.ShouldBe(["et_fast"]);
        filter.WorktimeExtent.ShouldBe(["wt_full"]);
        filter.Employer.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_RunsQThroughParser_ResidualReachesFilterNotRawInput()
    {
        // Residual-konsistens (parity GetFacetCounts): control chars stripped + whitespace
        // collapsed exactly like the list path, so the hint counts against the same WHERE.
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 3);
        var sut = CreateSut(search);

        await sut.Handle(new GetRemoteAdCountQuery(Q: "  utvecklare   distans  "), ct);

        search.LastFilter!.Q.ShouldBe("utvecklare distans");
    }

    [Fact]
    public async Task Handle_SubMinLengthQ_NormalizesToNull()
    {
        // #831 — the parser is now the SOLE guard on the minimum here: the validator's
        // MinimumLength rule was removed on all three parser-backed paths, so a sub-minimum
        // q reaches this handler instead of being rejected wire-side.
        //
        // This branch was UNTESTED on the remote-count path before #831. The sibling paths
        // both covered it (ListJobAds Handle_QNormalizesToSubMinLength_BecomesNull,
        // GetFacetCounts Handle_SubMinLengthQ_NormalizesToNull); this file only asserted the
        // whitespace-collapse branch above. Deleting a guard while its replacement has zero
        // coverage on the path is what this test exists to prevent — a fix for an untested
        // guarantee must itself be tested.
        //
        // Null, not "a": a nulled residual makes the count the unfiltered remote count, which
        // is the cheap indexed predicate. Asserting ShouldBeNull rather than ShouldNotBe("a")
        // pins the actual contract — an empty string would also satisfy the weaker form and
        // would reach SQL as a filter.
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 3);
        var sut = CreateSut(search);

        await sut.Handle(new GetRemoteAdCountQuery(Q: "a"), ct);

        search.LastFilter!.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ReturnsPortCountUnchanged_Wrapped()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 658);
        var sut = CreateSut(search);

        // Empty draft → the honest count of ALL remote ads (Remote=true still forced), never 0.
        var result = await sut.Handle(new GetRemoteAdCountQuery(), ct);

        result.Count.ShouldBe(658);
        search.CountCallCount.ShouldBe(1);
        search.LastFilter!.Remote.ShouldBeTrue();
    }
}
