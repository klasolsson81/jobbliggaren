using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 kriterie-vågen PR-2 — <see cref="BrowseCompaniesQueryValidator"/>.
///
/// <para>
/// <b>This suite is one HALF of a correctness guarantee, and it was missing</b> (code-reviewer Major,
/// 2026-07-13). The guarantee "the pager can never advertise a page it cannot serve" has two halves:
/// (a) the count saturates at <c>MaxPage × PageSize</c>, so <c>TotalPages ≤ MaxPage</c>; and (b) the
/// validator rejects <c>Page &gt; MaxPage</c>. Half (a) is pinned by
/// <c>CompanyWatchBrowseQueryTests</c>. Half (b) was pinned by NOTHING — it was asserted in a comment
/// and executed by no test, so swapping <c>InclusiveBetween(1, MaxPage)</c> for
/// <c>GreaterThanOrEqualTo(1)</c> would have left every test in the PR green while restoring the
/// unbounded-OFFSET DoS the cap exists to prevent.
/// </para>
/// </summary>
public class BrowseCompaniesQueryValidatorTests
{
    private static readonly BrowseCompaniesQueryValidator Validator = new();

    private static BrowseCompaniesQuery Query(int page = 1, int pageSize = 20, Guid? criterionId = null) =>
        new(criterionId ?? Guid.NewGuid(), page, pageSize);

    [Fact]
    public void Valid_bounds_pass()
    {
        Validator.Validate(Query(page: 1, pageSize: 1)).IsValid.ShouldBeTrue();
        Validator.Validate(Query(page: CompanyBrowseCriteria.MaxPage, pageSize: CompanyBrowseCriteria.MaxPageSize))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_below_one_is_rejected(int page) =>
        Validator.Validate(Query(page: page)).IsValid.ShouldBeFalse();

    [Fact]
    public void Page_above_MaxPage_is_rejected()
    {
        // The deep-offset ceiling. Without it, OFFSET 5_000_000 against a 1,17M-row register still makes
        // Postgres produce AND SORT every preceding row before discarding it — and TotalPages would
        // advertise pages the API answers 400 for.
        Validator.Validate(Query(page: CompanyBrowseCriteria.MaxPage + 1)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PageSize_below_one_is_rejected(int pageSize) =>
        Validator.Validate(Query(pageSize: pageSize)).IsValid.ShouldBeFalse();

    [Fact]
    public void PageSize_above_MaxPageSize_is_rejected() =>
        Validator.Validate(Query(pageSize: CompanyBrowseCriteria.MaxPageSize + 1)).IsValid.ShouldBeFalse();

    [Fact]
    public void Empty_criterion_id_is_rejected() =>
        Validator.Validate(Query(criterionId: Guid.Empty)).IsValid.ShouldBeFalse();
}
