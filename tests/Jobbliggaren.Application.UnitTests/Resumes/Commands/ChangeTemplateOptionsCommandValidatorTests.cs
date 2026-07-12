using Jobbliggaren.Application.Resumes.Commands.ChangeTemplateOptions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

// Fas 4b PR-8b 8b.2 — SmartEnum-driven validation: every member must resolve to its closed
// set by Name (no magic-string allow-lists), and a null/empty name yields a single message
// (Cascade.Stop guards the resolver from a null dictionary key).
public class ChangeTemplateOptionsCommandValidatorTests
{
    private readonly ChangeTemplateOptionsCommandValidator _validator = new();

    private static ChangeTemplateOptionsCommand Valid(
        string template = "Klar", string accent = "NavyBlue",
        string font = "Modern", string density = "Normal") =>
        new(Guid.NewGuid(), template, accent, font, density);

    [Fact]
    public void Valid_AllMembersResolve_Passes()
    {
        _validator.Validate(Valid("MorkPanel", "WineRed", "Classic", "Compact"))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Klar")]
    [InlineData("Accentlinje")]
    [InlineData("MorkPanel")]
    public void Valid_EveryTemplateName_Passes(string template) =>
        _validator.Validate(Valid(template: template)).IsValid.ShouldBeTrue();

    [Fact]
    public void EmptyResumeId_Fails()
    {
        var result = _validator.Validate(
            new ChangeTemplateOptionsCommand(Guid.Empty, "Klar", "NavyBlue", "Modern", "Normal"));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeTemplateOptionsCommand.ResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bogus")]
    [InlineData("klar")] // case-sensitive — the persisted vocabulary is exact
    public void InvalidTemplate_Fails(string template)
    {
        var result = _validator.Validate(Valid(template: template));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeTemplateOptionsCommand.Template));
    }

    [Fact]
    public void InvalidAccent_Fails() =>
        _validator.Validate(Valid(accent: "Teal")).IsValid.ShouldBeFalse();

    [Fact]
    public void InvalidFontPair_Fails() =>
        _validator.Validate(Valid(font: "Comic")).IsValid.ShouldBeFalse();

    [Fact]
    public void InvalidDensity_Fails() =>
        _validator.Validate(Valid(density: "Cramped")).IsValid.ShouldBeFalse();

    [Fact]
    public void EmptyTemplate_YieldsSingleError_NotDoubled()
    {
        // Cascade.Stop: a missing field is one "krävs" message, not "krävs" + a resolver
        // throw/second error.
        var result = _validator.Validate(Valid(template: ""));
        result.Errors.Count(e => e.PropertyName == nameof(ChangeTemplateOptionsCommand.Template))
            .ShouldBe(1);
    }
}
