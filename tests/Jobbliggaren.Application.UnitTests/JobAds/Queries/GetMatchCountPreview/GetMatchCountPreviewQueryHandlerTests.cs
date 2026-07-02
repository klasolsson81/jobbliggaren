using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0088) — the live search-preview count handler. Mirrors the
/// <see cref="GetFacetCounts.GetFacetCountsQueryHandlerTests"/> style: a hand-rolled ValueTask
/// fake for the <see cref="IJobAdSearchQuery"/> port (NSubstitute trips CA2012 at the ValueTask
/// call-setup site in this project), no DB. The handler's contract:
/// <list type="bullet">
/// <item>maps the four draft search dimensions to a <see cref="JobAdFilterCriteria"/>
/// (WorktimeExtent/Employer empty, Q null) — the pure search facets, no grade/profile;</item>
/// <item>counts via <see cref="IJobAdSearchQuery.CountAsync"/> (the anon-cacheable filter SPOT,
/// NOT the per-user grade port) and returns the port's count unchanged.</item>
/// </list>
/// </summary>
public class GetMatchCountPreviewQueryHandlerTests
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

        // The list/facet paths — never used by the preview-count handler.
        public ValueTask<PagedResult<JobAdDto>> SearchAsync(
            JobAdSearchCriteria criteria, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "SearchAsync ska inte anropas av preview-count-handlern — den counter:ar bara.");

        public ValueTask<IReadOnlyDictionary<string, int>> FacetCountsAsync(
            JobAdFilterCriteria criteria, FacetDimension dimension, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "FacetCountsAsync ska inte anropas av preview-count-handlern.");
    }

    private static GetMatchCountPreviewQuery Query(
        IReadOnlyList<string>? occ = null,
        IReadOnlyList<string>? reg = null,
        IReadOnlyList<string>? mun = null,
        IReadOnlyList<string>? emp = null) =>
        new(occ ?? [], reg ?? [], mun ?? [], emp ?? []);

    [Fact]
    public async Task Handle_ShouldReturnPortCountUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 1846);
        var sut = new GetMatchCountPreviewQueryHandler(search);

        var result = await sut.Handle(Query(occ: ["grp_12345"]), ct);

        // The counter number is the port's count verbatim — never massaged, never a mock.
        result.Count.ShouldBe(1846);
        search.CountCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldMapDraftDimensionsToFilterHardFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 5);
        var sut = new GetMatchCountPreviewQueryHandler(search);

        await sut.Handle(
            Query(
                occ: ["grp_dev"],
                reg: ["region_AB"],
                mun: ["kommun_0180"],
                emp: ["et_fast"]),
            ct);

        // Every draft dimension becomes a hard filter (parity a /jobb search) — this is why
        // every choice narrows monotonically, incl. ort+form without a yrke.
        search.LastFilter.ShouldNotBeNull();
        var filter = search.LastFilter!;
        filter.OccupationGroup.ShouldBe(["grp_dev"]);
        filter.Region.ShouldBe(["region_AB"]);
        filter.Municipality.ShouldBe(["kommun_0180"]);
        filter.EmploymentType.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task Handle_ShouldLeaveWorktimeEmployerAndQEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 5);
        var sut = new GetMatchCountPreviewQueryHandler(search);

        await sut.Handle(Query(occ: ["grp_dev"]), ct);

        // The setup draft exposes no worktime facet, no employer facet, and no free text
        // (no free-text ingress → no personnummer/PII surface).
        var filter = search.LastFilter!;
        filter.WorktimeExtent.ShouldBeEmpty();
        filter.Employer.ShouldBeEmpty();
        filter.Q.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldCountWholeCorpus_WhenDraftEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = new FakeJobAdSearchQuery(countToReturn: 38920);
        var sut = new GetMatchCountPreviewQueryHandler(search);

        // Empty draft → every dimension empty → the honest total (all active ads), NOT 0.
        var result = await sut.Handle(Query(), ct);

        result.Count.ShouldBe(38920);
        var filter = search.LastFilter!;
        filter.OccupationGroup.ShouldBeEmpty();
        filter.Region.ShouldBeEmpty();
        filter.Municipality.ShouldBeEmpty();
        filter.EmploymentType.ShouldBeEmpty();
    }
}
