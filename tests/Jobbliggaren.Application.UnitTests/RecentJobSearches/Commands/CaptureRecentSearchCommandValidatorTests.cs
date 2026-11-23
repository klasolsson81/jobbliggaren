using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.RecentJobSearches.Commands;

/// <summary>
/// #2285 (code-reviewer Minor 4) — the capture command's own sort rules, beside the axis rules it shares with the list
/// through <c>RecentSearchCandidateValidator</c> (driven in <c>TaxonomyAxisProfileIsInertTests</c>).
/// </summary>
public class CaptureRecentSearchCommandValidatorTests
{
    private static bool IsValid(CaptureRecentSearchCommand command) =>
        new CaptureRecentSearchCommandValidator().Validate(command).IsValid;

    [Fact]
    public void A_search_text_with_the_default_sort_is_valid() =>
        IsValid(new CaptureRecentSearchCommand(Q: "backend")).ShouldBeTrue();

    [Fact]
    public void A_relevance_sort_with_a_search_text_is_valid() =>
        IsValid(new CaptureRecentSearchCommand(Q: "backend", Sort: ListJobAdsSort.Relevance)).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_relevance_sort_without_a_search_text_is_invalid(string? q) =>
        IsValid(new CaptureRecentSearchCommand(Q: q, OccupationGroup: ["MVqp_eS8_kDZ"], Sort: ListJobAdsSort.Relevance))
            .ShouldBeFalse();

    [Fact]
    public void A_sort_outside_the_enum_is_invalid() =>
        IsValid(new CaptureRecentSearchCommand(Q: "backend", Sort: (ListJobAdsSort)99)).ShouldBeFalse();
}
