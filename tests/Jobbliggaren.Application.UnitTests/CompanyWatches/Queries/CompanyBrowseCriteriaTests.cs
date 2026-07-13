using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 kriterie-vågen PR-2 — <see cref="CompanyBrowseCriteria"/>'s constructor invariant.
///
/// <para>
/// <b>This suite exists because the fix for a "guarantee nobody tests" finding was itself a guarantee
/// nobody tested</b> (both review gates, independently, 2026-07-13). The paging cap was moved out of
/// <c>BrowseCompaniesQueryValidator</c> and into this constructor precisely because a validator only
/// guards callers that come through the Mediator pipeline — and the port's own doc anticipates one that
/// will not (PR-3's preview of an UNSAVED criterion). For that caller, the constructor is the ONLY lock.
/// And the lock had never been tried: deleting the <c>throw</c> expressions left all 17 146 Application
/// tests and all 181 Worker tests green.
/// </para>
///
/// <para>
/// The validator tests guard the front door. These guard the side door — the one the constructor was
/// added to cover in the first place.
/// </para>
/// </summary>
public class CompanyBrowseCriteriaTests
{
    private static readonly CompanyWatchCriteriaSpec Spec =
        CompanyWatchCriteriaSpec.FromTrusted(["62010"], ["0180"]);

    [Fact]
    public void Valid_bounds_construct()
    {
        Should.NotThrow(() => new CompanyBrowseCriteria(Spec, Page: 1, PageSize: 1));
        Should.NotThrow(() => new CompanyBrowseCriteria(
            Spec, CompanyBrowseCriteria.MaxPage, CompanyBrowseCriteria.MaxPageSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_below_one_throws(int page) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new CompanyBrowseCriteria(Spec, page, PageSize: 20));

    [Fact]
    public void Page_above_MaxPage_throws() =>
        // The deep-offset ceiling. Without this throw, a non-Mediator caller (PR-3's picker preview)
        // could ask for OFFSET 5_000_000 against a 1,17M-row register — and the pager's
        // TotalPages <= MaxPage guarantee, which the capped count buys, would evaporate with it.
        Should.Throw<ArgumentOutOfRangeException>(() => new CompanyBrowseCriteria(
            Spec, CompanyBrowseCriteria.MaxPage + 1, PageSize: 20));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageSize_below_one_throws(int pageSize) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new CompanyBrowseCriteria(Spec, Page: 1, pageSize));

    [Fact]
    public void PageSize_above_MaxPageSize_throws() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new CompanyBrowseCriteria(
            Spec, Page: 1, PageSize: CompanyBrowseCriteria.MaxPageSize + 1));

    [Fact]
    public void Null_criteria_throws() =>
        Should.Throw<ArgumentNullException>(() => new CompanyBrowseCriteria(null!, Page: 1, PageSize: 20));

    [Fact]
    public void MaxServableRows_is_derived_from_the_page_cap_never_a_hand_picked_number() =>
        // The count cap and the page cap are ONE knowledge piece ("how many rows can this surface ever
        // serve"). A standalone constant sitting next to an independent MaxPage is duplicated knowledge
        // that drifts apart — and the drift is a pager advertising pages it cannot serve.
        CompanyBrowseCriteria.MaxServableRows(20).ShouldBe(CompanyBrowseCriteria.MaxPage * 20);
}
