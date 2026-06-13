using Jobbliggaren.Application.SavedSearches.Commands.CreateSavedSearch;
using Jobbliggaren.Application.SavedSearches.Commands.UpdateSavedSearch;
using Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Validators;

// C2 (ADR 0067, CTO-dom (e)/(f) + architect F6): Create/UpdateSavedSearch
// byter Ssyk → OccupationGroup + Municipality. Validators speglar cap/regex
// per ny dimension (defense-in-depth; Domain-faktorn är sanningskällan).
// Copy per architect F1-tabellen. RÖD tills commands + validators bytt form.
public class CreateSavedSearchCommandValidatorTests
{
    private readonly CreateSavedSearchCommandValidator _validator = new();

    private static CreateSavedSearchCommand Command(
        string name = "Mitt sök",
        IReadOnlyList<string>? occupationGroup = null,
        IReadOnlyList<string>? municipality = null,
        IReadOnlyList<string>? region = null,
        string? q = null,
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc,
        bool notificationEnabled = false) =>
        new(name,
            OccupationGroup: occupationGroup,
            Municipality: municipality,
            Region: region,
            EmploymentType: null,
            WorktimeExtent: null,
            Q: q,
            SortBy: sortBy,
            NotificationEnabled: notificationEnabled);

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(Command(occupationGroup: ["grp_12345"]));
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithEmptyName_IsInvalid(string name)
    {
        var result = _validator.Validate(Command(name: name, occupationGroup: ["grp_12345"]));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithNameTooLong_IsInvalid()
    {
        var result = _validator.Validate(Command(
            name: new string('x', SavedSearch.NameMaxLength + 1),
            occupationGroup: ["grp_12345"]));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithUndefinedSortBy_IsInvalid()
    {
        var result = _validator.Validate(Command(
            occupationGroup: ["grp_12345"], sortBy: (JobAdSortBy)999));
        result.IsValid.ShouldBeFalse();
    }

    // ---- OccupationGroup — cap + per-element-regex ------------------------

    [Fact]
    public void Validate_OccupationGroup_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = _validator.Validate(Command(occupationGroup: overMax));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.ErrorMessage == $"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");
    }

    [Fact]
    public void Validate_OccupationGroup_ExactlyMax_IsValid()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();

        var result = _validator.Validate(Command(occupationGroup: max));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken
    public void Validate_OccupationGroup_AnyInvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(occupationGroup: ["grp_ok", bad]));
        result.IsValid.ShouldBeFalse();
    }

    // ---- Municipality — cap + per-element-regex ---------------------------

    [Fact]
    public void Validate_Municipality_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();

        var result = _validator.Validate(Command(municipality: overMax));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.ErrorMessage == $"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    public void Validate_Municipality_AnyInvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(municipality: ["sthlm_kn", bad]));
        result.IsValid.ShouldBeFalse();
    }

    // ---- Region — oförändrad dimension ------------------------------------

    [Fact]
    public void Validate_Region_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = _validator.Validate(Command(region: overMax));

        result.IsValid.ShouldBeFalse();
    }
}

public class UpdateSavedSearchCommandValidatorTests
{
    private readonly UpdateSavedSearchCommandValidator _validator = new();

    private static SavedSearchCriteriaInput Criteria(
        IReadOnlyList<string>? occupationGroup = null,
        IReadOnlyList<string>? municipality = null,
        IReadOnlyList<string>? region = null,
        string? q = null,
        JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc) =>
        new(OccupationGroup: occupationGroup,
            Municipality: municipality,
            Region: region,
            EmploymentType: null,
            WorktimeExtent: null,
            Q: q,
            SortBy: sortBy);

    [Fact]
    public void Validate_WithSingleFieldChange_IsValid()
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), "Nytt namn", null, null));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoFieldsChanged_IsInvalid()
    {
        // PATCH med inga fält = vilseledande no-op (skulle skriva audit-rad).
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null, null));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyId_IsInvalid()
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.Empty, "Namn", null, null));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyNameWhenProvided_IsInvalid()
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), "", null, null));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithUndefinedSortByInCriteria_IsInvalid()
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null,
            Criteria(occupationGroup: ["grp_12345"], sortBy: (JobAdSortBy)999)));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithValidOccupationGroupAndMunicipalityCriteria_IsValid()
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null,
            Criteria(occupationGroup: ["grp_12345"], municipality: ["sthlm_kn"])));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_CriteriaOccupationGroup_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null, Criteria(occupationGroup: overMax)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.ErrorMessage == $"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    public void Validate_CriteriaOccupationGroup_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null, Criteria(occupationGroup: [bad])));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_CriteriaMunicipality_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();

        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null, Criteria(municipality: overMax)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.ErrorMessage == $"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    public void Validate_CriteriaMunicipality_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(new UpdateSavedSearchCommand(
            Guid.NewGuid(), null, null, Criteria(municipality: [bad])));
        result.IsValid.ShouldBeFalse();
    }
}

public class RunSavedSearchQueryValidatorTests
{
    private readonly RunSavedSearchQueryValidator _validator = new();

    [Fact]
    public void Validate_WithDefaults_IsValid()
    {
        var result = _validator.Validate(new RunSavedSearchQuery(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEmptyId_IsInvalid()
    {
        var result = _validator.Validate(new RunSavedSearchQuery(Guid.Empty));
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageBelowOne_IsInvalid(int page)
    {
        var result = _validator.Validate(new RunSavedSearchQuery(Guid.NewGuid(), Page: page));
        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutsideRange_IsInvalid(int pageSize)
    {
        var result = _validator.Validate(
            new RunSavedSearchQuery(Guid.NewGuid(), PageSize: pageSize));
        result.IsValid.ShouldBeFalse();
    }
}
