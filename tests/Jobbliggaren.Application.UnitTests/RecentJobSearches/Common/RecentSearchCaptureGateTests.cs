using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Common;

/// <summary>
/// #2199 PR 2b — the gate on its own, through the query production sends it. The behaviour's tests cover each
/// refusal through the capture; these pin what <see cref="RecentSearchCaptureGate.Admit"/> answers, which is
/// also what the list page reports. The Create-failure arm is left untested on purpose: no query the
/// validator lets through reaches it (the comment at that arm).
/// </summary>
public sealed class RecentSearchCaptureGateTests
{
    private static readonly RecentSearchCaptureGate Gate = RealRecentSearchCaptureGate.Instance;

    [Fact]
    public void Admit_AnOrdinarySearch_ReturnsTheCriteriaCreateBuilds()
    {
        var admitted = Gate.Admit(new ListJobAdsQuery(
            Q: "backend", OccupationGroup: ["DJh5_yyF_hEM"], Municipality: ["AvNB_uwa_6n6"]));

        admitted.ShouldBe(SearchCriteria.Create(
            occupationGroup: ["DJh5_yyF_hEM"], occupationName: [], municipality: ["AvNB_uwa_6n6"], region: [],
            employmentType: [], worktimeExtent: [], employer: [], remote: false, q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value);
    }

    [Fact]
    public void Admit_ADefaultBrowse_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery()).ShouldBeNull();

    [Fact]
    public void Admit_APersonnummerShapedEmployer_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery(Employer: ["8112189876"])).ShouldBeNull();

    [Fact]
    public void Admit_APersonnummerInQ_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery(Q: "backend 811218-9876")).ShouldBeNull();

    [Fact]
    public void Admit_APersonnummerOnATaxonomyAxis_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery(Municipality: ["8112189876"])).ShouldBeNull();

    [Fact]
    public void Admit_AReligiousGroup_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery(OccupationGroup: [ReligiousOccupationGroups.Deacons])).ShouldBeNull();

    // Imam, filed under Präster in the lineage artefact.
    [Fact]
    public void Admit_AReligiousName_ReturnsNull() =>
        Gate.Admit(new ListJobAdsQuery(OccupationName: ["5avt_87C_rXE"])).ShouldBeNull();

    // A gate the list handler calls on every request must not throw on what a request can carry.
    [Fact]
    public void Admit_ARelevanceSortWithoutTerms_StoresTheSortThatRan()
    {
        var admitted = Gate.Admit(new ListJobAdsQuery(
            Sort: ListJobAdsSort.Relevance, OccupationGroup: ["DJh5_yyF_hEM"]));

        admitted.ShouldNotBeNull();
        admitted.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
    }
}
