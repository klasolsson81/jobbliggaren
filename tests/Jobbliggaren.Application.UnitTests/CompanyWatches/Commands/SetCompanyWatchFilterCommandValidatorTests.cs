using Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// Bevakning F4a (#803, BC-5) — the validator is a TRANSPORT guard: it stops a hostile or absurd
/// payload before the domain, and stops nothing else. The value rules (concept-id format,
/// normalization, the empty-spec invariant) live in <c>WatchFilterSpec</c> and must NOT be duplicated
/// here — so the load-bearing assertion is the one that says an ALL-EMPTY selection is VALID: it is
/// how the user clears the filter, and a NotEmpty() slip here would make "turn the filter off"
/// unreachable from the API.
/// </summary>
public class SetCompanyWatchFilterCommandValidatorTests
{
    private readonly SetCompanyWatchFilterCommandValidator _validator = new();

    private static SetCompanyWatchFilterCommand Command(
        Guid? watchId = null,
        IReadOnlyList<string>? municipalities = null,
        IReadOnlyList<string>? regions = null,
        bool onlyMatched = false) =>
        new(watchId ?? Guid.NewGuid(), municipalities ?? [], regions ?? [], onlyMatched);

    [Fact]
    public void Validate_WithSelectionOnBothAxes_Passes()
    {
        var result = _validator.Validate(
            Command(municipalities: ["kommun_a"], regions: ["lan_skane"], onlyMatched: true));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithAllEmptySelection_Passes()
    {
        // The clear path. An empty selection is a VALUE ("no filter"), not a missing field.
        var result = _validator.Validate(Command());

        result.IsValid.ShouldBeTrue(
            "ett tomt val är hur användaren rensar filtret — validatorn får aldrig blockera det");
    }

    [Fact]
    public void Validate_WithEmptyCompanyWatchId_Fails()
    {
        var result = _validator.Validate(Command(watchId: Guid.Empty));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMoreThanMaxConceptIdsMunicipalities_Fails()
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn_{i}").ToList();

        var result = _validator.Validate(Command(municipalities: tooMany));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithMoreThanMaxConceptIdsRegions_Fails()
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"lan_{i}").ToList();

        var result = _validator.Validate(Command(regions: tooMany));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_AtMaxConceptIdsOnBothAxes_Passes()
    {
        // The cap is PER AXIS, at the same SSOT constant the VO uses — the boundary value itself is
        // legal (an off-by-one here would reject a legitimate full-country selection).
        var municipalities = Enumerable.Range(0, SearchCriteria.MaxConceptIds)
            .Select(i => $"kn_{i}").ToList();
        var regions = Enumerable.Range(0, SearchCriteria.MaxConceptIds)
            .Select(i => $"lan_{i}").ToList();

        var result = _validator.Validate(Command(municipalities: municipalities, regions: regions));

        result.IsValid.ShouldBeTrue();
    }
}
