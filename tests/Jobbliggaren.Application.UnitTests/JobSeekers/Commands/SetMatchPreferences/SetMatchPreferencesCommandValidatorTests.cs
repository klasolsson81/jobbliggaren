using Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetMatchPreferences;

// F4-12 (CTO-frozen) — SetMatchPreferencesCommandValidator speglar
// CreateSavedSearchCommandValidator:s defense-in-depth (per-element concept-id-
// pattern + per-list-cap mot SearchCriteria.MaxConceptIds). MEDVETET AVSTEG:
// INGEN "minst ett kriterium"-regel — alla listor tomma/null = GILTIGT.
// Domänen (MatchPreferences.Create) är sanningskällan; validatorn är tidig
// 400-grind.
//
// RÖD tills SetMatchPreferencesCommand + validatorn finns.
public class SetMatchPreferencesCommandValidatorTests
{
    private readonly SetMatchPreferencesCommandValidator _validator = new();

    private static SetMatchPreferencesCommand Command(
        IReadOnlyList<string>? preferredOccupationGroups = null,
        IReadOnlyList<string>? preferredRegions = null,
        IReadOnlyList<string>? preferredEmploymentTypes = null,
        IReadOnlyList<string>? preferredMunicipalities = null,
        IReadOnlyList<string>? preferredSkills = null,
        int? experienceYears = null,
        IReadOnlyList<OccupationExperienceInput>? preferredOccupationExperience = null) =>
        new(
            PreferredOccupationGroups: preferredOccupationGroups,
            PreferredRegions: preferredRegions,
            PreferredEmploymentTypes: preferredEmploymentTypes,
            PreferredMunicipalities: preferredMunicipalities,
            PreferredSkills: preferredSkills,
            ExperienceYears: experienceYears,
            PreferredOccupationExperience: preferredOccupationExperience);

    [Fact]
    public void Validate_WithValidConceptIds_Passes()
    {
        var result = _validator.Validate(Command(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast", "et_vikariat"],
            preferredMunicipalities: ["sthlm_kn"]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithAllNull_Passes()
    {
        // MEDVETET AVSTEG mot CreateSavedSearch — tomma/null-listor är GILTIGT
        // (ingen "minst ett"-regel). Inkl. municipality (4:e optional dim).
        var result = _validator.Validate(Command());

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithAllEmptyLists_Passes()
    {
        var result = _validator.Validate(Command(
            preferredOccupationGroups: [],
            preferredRegions: [],
            preferredEmploymentTypes: [],
            preferredMunicipalities: []));

        result.IsValid.ShouldBeTrue();
    }

    // Spår 3 PR-A — municipality-only valid set passerar (4:e peer-dimension).
    [Fact]
    public void Validate_WithValidMunicipalitiesOnly_Passes()
    {
        var result = _validator.Validate(Command(
            preferredMunicipalities: ["sthlm_kn", "gbg_kn"]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationGroups_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"grp{i}").ToArray();

        var result = _validator.Validate(Command(preferredOccupationGroups: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Regions_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = _validator.Validate(Command(preferredRegions: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_EmploymentTypes_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"et{i}").ToArray();

        var result = _validator.Validate(Command(preferredEmploymentTypes: overMax));

        result.IsValid.ShouldBeFalse();
    }

    // Spår 3 PR-A — municipality cap = SearchCriteria.MaxConceptIds.
    [Fact]
    public void Validate_Municipalities_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"kn{i}").ToArray();

        var result = _validator.Validate(Command(preferredMunicipalities: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("has space")]
    [InlineData("åäö")]
    public void Validate_OccupationGroups_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(preferredOccupationGroups: ["grp1", bad]));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("region space")]
    [InlineData("dot.notation")]
    public void Validate_Regions_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(preferredRegions: ["stockholm_AB", bad]));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("0123456789ABCDEFabcdef_-_-_-_-123")] // 33 tecken
    public void Validate_EmploymentTypes_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(preferredEmploymentTypes: ["et_fast", bad]));

        result.IsValid.ShouldBeFalse();
    }

    // Spår 3 PR-A — municipality per-element regex (default-deny).
    [Theory]
    [InlineData("bad id!")]
    [InlineData("kommun space")]
    [InlineData("dot.notation")]
    [InlineData("åäö")]
    public void Validate_Municipalities_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(preferredMunicipalities: ["sthlm_kn", bad]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ExactlyMaxPerList_Passes()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"grp{i}").ToArray();

        var result = _validator.Validate(Command(preferredOccupationGroups: max));

        result.IsValid.ShouldBeTrue();
    }

    // STEG 3 (ADR 0079) — skills cap + per-element regex (defense-in-depth).

    [Fact]
    public void Validate_WithValidSkillsOnly_Passes()
    {
        var result = _validator.Validate(Command(preferredSkills: ["skill_java", "skill_spring"]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Skills_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"sk{i}").ToArray();

        var result = _validator.Validate(Command(preferredSkills: overMax));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("skill space")]
    [InlineData("dot.notation")]
    [InlineData("åäö")]
    public void Validate_Skills_InvalidElement_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(preferredSkills: ["skill_java", bad]));

        result.IsValid.ShouldBeFalse();
    }

    // STEG 3 (ADR 0079) — experience years 0..70 when present; null/0/70 valid,
    // negative / over-70 invalid.

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(70)]
    public void Validate_WithExperienceYearsInRange_Passes(int years)
    {
        _validator.Validate(Command(experienceYears: years)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNullExperienceYears_Passes()
    {
        _validator.Validate(Command(experienceYears: null)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    [InlineData(1000)]
    public void Validate_WithExperienceYearsOutOfRange_IsInvalid(int years)
    {
        _validator.Validate(Command(experienceYears: years)).IsValid.ShouldBeFalse();
    }

    // ADR 0079-amendment (exp-per-occ PR-3) — the per-occupation experience overlay:
    // cap + per-entry concept-id pattern + per-entry years range (defense-in-depth).

    [Fact]
    public void Validate_WithValidOccupationExperience_Passes()
    {
        var result = _validator.Validate(Command(
            preferredOccupationGroups: ["grp_12345"],
            preferredOccupationExperience:
            [
                new OccupationExperienceInput("grp_12345", 5),
                new OccupationExperienceInput("grp_67890", null), // null years = not stated, valid
            ]));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OccupationExperience_OneOverMax_IsInvalid()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => new OccupationExperienceInput($"grp{i}", 1)).ToArray();

        _validator.Validate(Command(preferredOccupationExperience: overMax)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("grp space")]
    [InlineData("åäö")]
    public void Validate_OccupationExperience_InvalidConceptId_IsInvalid(string bad)
    {
        var result = _validator.Validate(Command(
            preferredOccupationExperience: [new OccupationExperienceInput(bad, 5)]));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    [InlineData(1000)]
    public void Validate_OccupationExperience_YearsOutOfRange_IsInvalid(int years)
    {
        var result = _validator.Validate(Command(
            preferredOccupationExperience: [new OccupationExperienceInput("grp_12345", years)]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_OccupationExperience_NullYears_Passes()
    {
        var result = _validator.Validate(Command(
            preferredOccupationExperience: [new OccupationExperienceInput("grp_12345", null)]));

        result.IsValid.ShouldBeTrue();
    }
}
