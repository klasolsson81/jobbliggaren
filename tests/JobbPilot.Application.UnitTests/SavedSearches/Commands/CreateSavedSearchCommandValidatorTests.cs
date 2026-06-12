using JobbPilot.Application.SavedSearches.Commands.CreateSavedSearch;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Application.UnitTests.SavedSearches.Commands;

// B2 (ADR 0067 Beslut 6/7) — CreateSavedSearchCommandValidator får defense-in-
// depth-paritet för EmploymentType + WorktimeExtent (samma cap + per-element-
// regex som OccupationGroup; Domain SearchCriteria = sanningskälla). Dedikerad
// validator-test (fanns inte tidigare) — handler-testet täcker handler-vägen,
// inte validator-reglerna.
//
// RÖD tills CreateSavedSearchCommand får EmploymentType/WorktimeExtent-props +
// validatorn fått de nya reglerna.
public class CreateSavedSearchCommandValidatorTests
{
    private readonly CreateSavedSearchCommandValidator _validator = new();

    private static CreateSavedSearchCommand Command(
        IReadOnlyList<string>? occupationGroup = null,
        IReadOnlyList<string>? employmentType = null,
        IReadOnlyList<string>? worktimeExtent = null) =>
        new(
            Name: "Min sökning",
            OccupationGroup: occupationGroup,
            Municipality: null,
            Region: null,
            EmploymentType: employmentType,
            WorktimeExtent: worktimeExtent,
            Q: "backend",
            SortBy: JobAdSortBy.PublishedAtDesc,
            NotificationEnabled: false);

    [Fact]
    public void Validate_WithValidEmploymentTypeAndWorktimeExtent_Passes()
    {
        var result = _validator.Validate(Command(
            employmentType: ["et_fast", "et_vikariat"], worktimeExtent: ["wt_heltid"]));

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
    [InlineData("åäö")]
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
    public void Validate_NewDimensions_Null_Passes()
    {
        // null = ej angivet → ingen regel triggas (paritet med OccupationGroup).
        var result = _validator.Validate(Command(employmentType: null, worktimeExtent: null));

        result.IsValid.ShouldBeTrue();
    }
}
