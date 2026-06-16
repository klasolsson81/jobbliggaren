using Jobbliggaren.Application.SavedSearches.Commands.ConfirmDerivedSearch;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Commands;

// Fas 4 STEG B — input-shape validation for ConfirmDerivedSearch: a confirmed derived search must
// carry a name and at least one confirmed occupation group (the distinguishing semantic). Per-id
// format/cap is enforced by SearchCriteria.Create in the handler, not here.
public class ConfirmDerivedSearchCommandValidatorTests
{
    private readonly ConfirmDerivedSearchCommandValidator _validator = new();

    private static ConfirmDerivedSearchCommand Command(string name, IReadOnlyList<string> occupationGroup) =>
        new(name, occupationGroup, null, null, null, null, null, null, JobAdSortBy.PublishedAtDesc, true);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.Validate(Command("CV-sök", ["grp_12345"])).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        var result = _validator.Validate(Command("", ["grp_12345"]));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmDerivedSearchCommand.Name));
    }

    [Fact]
    public void Validate_EmptyOccupationGroup_Fails()
    {
        var result = _validator.Validate(Command("CV-sök", []));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmDerivedSearchCommand.OccupationGroup));
    }

    [Fact]
    public void Validate_NameOver120Chars_Fails()
    {
        var result = _validator.Validate(Command(new string('a', 121), ["grp_12345"]));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmDerivedSearchCommand.Name));
    }

    [Fact]
    public void Validate_NameExactly120Chars_Passes()
    {
        _validator.Validate(Command(new string('a', 120), ["grp_12345"])).IsValid.ShouldBeTrue();
    }
}
