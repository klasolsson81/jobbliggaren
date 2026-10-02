using FluentValidation.Results;
using Jobbliggaren.Application.JobSeekers.Commands;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.UpdateMatchPreferences;

// #1918 — the per-part write's early 400 gate. A request must carry at least one part;
// inside a present part every list must be non-null ([] is the only way to clear).
// MatchPreferences.Create stays the authoritative source, including the subset and distinct rules.
//
// A member MISSING from a present part is refused at binding ([JsonRequired]) and never reaches the
// validator, so it has no row here: the Api integration tests carry it on the wire, and
// MatchPreferencesContractParityTests pins which members carry the attribute.
public class UpdateMatchPreferencesCommandValidatorTests
{
    public enum Part { Occupations, Skills, Locations, EmploymentTypes, Experience }

    public enum ConceptList { OccupationGroups, Skills, Regions, Municipalities, EmploymentTypes }

    private readonly UpdateMatchPreferencesCommandValidator _validator = new();

    private static UpdateMatchPreferencesCommand OnlyPart(Part part) => part switch
    {
        Part.Occupations => new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(
                PreferredOccupationGroups: ["grp_a"],
                PreferredOccupationExperience: [new OccupationExperienceInput("grp_a", 4)])),
        Part.Skills => new UpdateMatchPreferencesCommand(
            Skills: new SkillsPartInput(PreferredSkills: ["sk_a"])),
        Part.Locations => new UpdateMatchPreferencesCommand(
            Locations: new LocationsPartInput(
                PreferredRegions: ["reg_a"], PreferredMunicipalities: ["kn_a"], PreferredRemote: true)),
        Part.EmploymentTypes => new UpdateMatchPreferencesCommand(
            EmploymentTypes: new EmploymentTypesPartInput(PreferredEmploymentTypes: ["et_a"])),
        Part.Experience => new UpdateMatchPreferencesCommand(
            Experience: new ExperiencePartInput(ExperienceYears: 5)),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    // A command carrying only the part that holds this list, with the part's other lists empty.
    private static UpdateMatchPreferencesCommand WithList(ConceptList list, IReadOnlyList<string> ids) => list switch
    {
        ConceptList.OccupationGroups => new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(PreferredOccupationGroups: ids)),
        ConceptList.Skills => new UpdateMatchPreferencesCommand(
            Skills: new SkillsPartInput(PreferredSkills: ids)),
        ConceptList.Regions => new UpdateMatchPreferencesCommand(
            Locations: new LocationsPartInput(
                PreferredRegions: ids, PreferredMunicipalities: [], PreferredRemote: false)),
        ConceptList.Municipalities => new UpdateMatchPreferencesCommand(
            Locations: new LocationsPartInput(
                PreferredRegions: [], PreferredMunicipalities: ids, PreferredRemote: false)),
        ConceptList.EmploymentTypes => new UpdateMatchPreferencesCommand(
            EmploymentTypes: new EmploymentTypesPartInput(PreferredEmploymentTypes: ids)),
        _ => throw new ArgumentOutOfRangeException(nameof(list), list, null),
    };

    private static string MemberOf(ConceptList list) => list switch
    {
        ConceptList.OccupationGroups => nameof(OccupationsPartInput.PreferredOccupationGroups),
        ConceptList.Skills => nameof(SkillsPartInput.PreferredSkills),
        ConceptList.Regions => nameof(LocationsPartInput.PreferredRegions),
        ConceptList.Municipalities => nameof(LocationsPartInput.PreferredMunicipalities),
        ConceptList.EmploymentTypes => nameof(EmploymentTypesPartInput.PreferredEmploymentTypes),
        _ => throw new ArgumentOutOfRangeException(nameof(list), list, null),
    };

    private static UpdateMatchPreferencesCommand WithYears(
        IReadOnlyList<OccupationExperienceInput> years, IReadOnlyList<string>? groups = null) =>
        new(Occupations: new OccupationsPartInput(
            PreferredOccupationGroups: groups ?? ["grp_a"], PreferredOccupationExperience: years));

    // The failing rule is the one on this member: the 400 body is keyed by the property name.
    private static void ShouldFailOn(ValidationResult result, string member)
    {
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(
            e => e.PropertyName.Contains(member, StringComparison.Ordinal),
            $"expected a failure on {member}; got: {string.Join(", ", result.Errors.Select(e => e.PropertyName))}");
    }

    private static string[] Ids(int count, string prefix) =>
        [.. Enumerable.Range(1, count).Select(i => $"{prefix}{i}")];

    [Fact]
    public void Validate_WithNoPartPresent_IsInvalid()
    {
        _validator.Validate(new UpdateMatchPreferencesCommand()).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(Part.Occupations)]
    [InlineData(Part.Skills)]
    [InlineData(Part.Locations)]
    [InlineData(Part.EmploymentTypes)]
    [InlineData(Part.Experience)]
    public void Validate_WithOneValidPart_Passes(Part part)
    {
        var result = _validator.Validate(OnlyPart(part));

        result.IsValid.ShouldBeTrue(string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Validate_WithEveryPartPresentAndValid_Passes()
    {
        var command = new UpdateMatchPreferencesCommand(
            Occupations: OnlyPart(Part.Occupations).Occupations,
            Skills: OnlyPart(Part.Skills).Skills,
            Locations: OnlyPart(Part.Locations).Locations,
            EmploymentTypes: OnlyPart(Part.EmploymentTypes).EmploymentTypes,
            Experience: OnlyPart(Part.Experience).Experience);

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    // The years overlay is the one optional member of a part.
    [Fact]
    public void Validate_WithOccupationsWithoutYears_Passes()
    {
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(PreferredOccupationGroups: ["grp_a"]));

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    // [] is the only way to clear a list, so every empty list must pass.
    [Fact]
    public void Validate_WithEveryListEmpty_Passes()
    {
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(PreferredOccupationGroups: [], PreferredOccupationExperience: []),
            Skills: new SkillsPartInput(PreferredSkills: []),
            Locations: new LocationsPartInput(PreferredRegions: [], PreferredMunicipalities: [], PreferredRemote: false),
            EmploymentTypes: new EmploymentTypesPartInput(PreferredEmploymentTypes: []));

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    // An explicit JSON null passes [JsonRequired] (the member is present) and System.Text.Json binds it
    // as a null list: "preferredSkills": null. The validator is what refuses it.
    [Theory]
    [InlineData(ConceptList.OccupationGroups)]
    [InlineData(ConceptList.Skills)]
    [InlineData(ConceptList.Regions)]
    [InlineData(ConceptList.Municipalities)]
    [InlineData(ConceptList.EmploymentTypes)]
    public void Validate_WithAPresentPartWhoseListIsNull_IsInvalid(ConceptList list)
    {
        var result = _validator.Validate(WithList(list, null!));

        ShouldFailOn(result, MemberOf(list));
    }

    [Theory]
    [InlineData(ConceptList.OccupationGroups)]
    [InlineData(ConceptList.Skills)]
    [InlineData(ConceptList.Regions)]
    [InlineData(ConceptList.Municipalities)]
    [InlineData(ConceptList.EmploymentTypes)]
    public void Validate_WithAListAtTheCap_Passes(ConceptList list)
    {
        _validator.Validate(WithList(list, Ids(SearchCriteria.MaxConceptIds, "id"))).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ConceptList.OccupationGroups)]
    [InlineData(ConceptList.Skills)]
    [InlineData(ConceptList.Regions)]
    [InlineData(ConceptList.Municipalities)]
    [InlineData(ConceptList.EmploymentTypes)]
    public void Validate_WithAListOneOverTheCap_IsInvalid(ConceptList list)
    {
        var result = _validator.Validate(WithList(list, Ids(SearchCriteria.MaxConceptIds + 1, "id")));

        ShouldFailOn(result, MemberOf(list));
    }

    [Theory]
    [InlineData(ConceptList.OccupationGroups, "bad id!")]
    [InlineData(ConceptList.OccupationGroups, "åäö")]
    [InlineData(ConceptList.Skills, "has space")]
    [InlineData(ConceptList.Skills, "123456789012345678901234567890123")]
    [InlineData(ConceptList.Regions, "dot.notation")]
    [InlineData(ConceptList.Municipalities, "semi;colon")]
    [InlineData(ConceptList.EmploymentTypes, "plus+sign")]
    public void Validate_WithAnIdOutsideTheConceptIdFormat_IsInvalid(ConceptList list, string bad)
    {
        var result = _validator.Validate(WithList(list, ["id_ok", bad]));

        ShouldFailOn(result, MemberOf(list));
    }

    // Every annotated group is chosen and the group list sits at its own cap, so only the overlay's
    // length can decide these two rows.
    [Fact]
    public void Validate_WithYearsAtTheCap_Passes()
    {
        var groups = Ids(SearchCriteria.MaxConceptIds, "grp");
        var years = groups.Select(id => new OccupationExperienceInput(id, 1)).ToArray();

        _validator.Validate(WithYears(years, groups)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithYearsOneOverTheCap_IsInvalid()
    {
        var groups = Ids(SearchCriteria.MaxConceptIds, "grp");
        var years = groups
            .Select(id => new OccupationExperienceInput(id, 1))
            .Append(new OccupationExperienceInput("grp1", 2))
            .ToArray();

        ShouldFailOn(
            _validator.Validate(WithYears(years, groups)),
            nameof(OccupationsPartInput.PreferredOccupationExperience));
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("åäö")]
    public void Validate_WithAYearsEntryOutsideTheConceptIdFormat_IsInvalid(string bad)
    {
        var result = _validator.Validate(WithYears([new OccupationExperienceInput(bad, 4)]));

        ShouldFailOn(result, nameof(OccupationsPartInput.PreferredOccupationExperience));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(MatchPreferences.MaxExperienceYears + 1)]
    public void Validate_WithAYearsEntryOutOfRange_IsInvalid(int years)
    {
        var result = _validator.Validate(WithYears([new OccupationExperienceInput("grp_a", years)]));

        ShouldFailOn(result, nameof(OccupationsPartInput.PreferredOccupationExperience));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MatchPreferences.MaxExperienceYears)]
    [InlineData(null)]
    public void Validate_WithAYearsEntryInRangeOrNotStated_Passes(int? years)
    {
        _validator.Validate(WithYears([new OccupationExperienceInput("grp_a", years)])).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(MatchPreferences.MaxExperienceYears + 1)]
    [InlineData(1000)]
    public void Validate_WithExperienceYearsOutOfRange_IsInvalid(int years)
    {
        var result = _validator.Validate(
            new UpdateMatchPreferencesCommand(Experience: new ExperiencePartInput(ExperienceYears: years)));

        ShouldFailOn(result, nameof(ExperiencePartInput.ExperienceYears));
    }

    // null clears the stated years; 0 is a stated zero, distinct from null.
    [Theory]
    [InlineData(0)]
    [InlineData(MatchPreferences.MaxExperienceYears)]
    [InlineData(null)]
    public void Validate_WithExperienceYearsInRangeOrNull_Passes(int? years)
    {
        var result = _validator.Validate(
            new UpdateMatchPreferencesCommand(Experience: new ExperiencePartInput(ExperienceYears: years)));

        result.IsValid.ShouldBeTrue();
    }
}
