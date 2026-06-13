using Jobbliggaren.Application.SavedSearches.Commands.UpdateSavedSearch;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Commands;

// B2 (ADR 0067 Beslut 6/7) — UpdateSavedSearchCommandValidator får defense-in-
// depth-paritet för EmploymentType + WorktimeExtent på SavedSearchCriteriaInput
// (cap + per-element-regex inom When(Criteria != null)-blocket, paritet med
// OccupationGroup). Dedikerad validator-test (fanns inte tidigare).
//
// RÖD tills SavedSearchCriteriaInput får EmploymentType/WorktimeExtent-props +
// validatorn fått de nya reglerna.
public class UpdateSavedSearchCommandValidatorTests
{
    private readonly UpdateSavedSearchCommandValidator _validator = new();

    private static UpdateSavedSearchCommand Command(
        IReadOnlyList<string>? employmentType = null,
        IReadOnlyList<string>? worktimeExtent = null) =>
        new(
            Id: Guid.NewGuid(),
            Name: null,
            NotificationEnabled: null,
            Criteria: new SavedSearchCriteriaInput(
                OccupationGroup: ["grp1"],
                Municipality: null,
                Region: null,
                EmploymentType: employmentType,
                WorktimeExtent: worktimeExtent,
                Q: "backend",
                SortBy: JobAdSortBy.PublishedAtDesc));

    [Fact]
    public void Validate_WithValidNewDimensions_Passes()
    {
        var result = _validator.Validate(Command(
            employmentType: ["et_fast"], worktimeExtent: ["wt_heltid", "wt_deltid"]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmploymentType_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"et{i}").ToArray();

        var result = _validator.Validate(Command(employmentType: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("has space")]
    public void Validate_EmploymentType_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(employmentType: ["et_fast", bad]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WorktimeExtent_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"wt{i}").ToArray();

        var result = _validator.Validate(Command(worktimeExtent: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")] // 33 tecken
    public void Validate_WorktimeExtent_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(worktimeExtent: ["wt_heltid", bad]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_NewDimensions_NullWithinCriteria_Passes()
    {
        var result = _validator.Validate(Command(employmentType: null, worktimeExtent: null));

        result.IsValid.ShouldBeTrue();
    }
}
