using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResumePreview;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4b PR-8b 8b.3 — input-shape validation for the ephemeral-preview query. Mirrors
/// <c>ChangeTemplateOptionsCommandValidator</c> (the write-path validator): the ResumeId must be a
/// non-empty Guid and each of the four visual option names must resolve fail-loud to its closed
/// SmartEnum set (no magic-string lists, §5). Preview and save reject identical bad input identically.
/// </summary>
public class RenderResumePreviewQueryValidatorTests
{
    private readonly RenderResumePreviewQueryValidator _validator = new();

    private static RenderResumePreviewQuery Query(
        Guid? id = null,
        string? template = null,
        string? accent = null,
        string? font = null,
        string? density = null) =>
        new(id ?? Guid.NewGuid(),
            template ?? CvTemplate.Klar.Name,
            accent ?? CvAccentColor.NavyBlue.Name,
            font ?? CvFontPair.Modern.Name,
            density ?? CvDensity.Normal.Name);

    [Fact]
    public void Validate_ValidQuery_Passes()
    {
        _validator.Validate(Query(
            template: CvTemplate.MorkPanel.Name,
            accent: CvAccentColor.WineRed.Name,
            font: CvFontPair.Classic.Name,
            density: CvDensity.Airy.Name)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyResumeId_Fails()
    {
        var result = _validator.Validate(Query(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumePreviewQuery.ResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("klar")]        // wrong case — SmartEnum.TryFromName is case-sensitive
    [InlineData("NotATemplate")]
    [InlineData("1")]           // numeric never resolves a SmartEnum by name
    public void Validate_InvalidTemplate_Fails(string template)
    {
        var result = _validator.Validate(Query(template: template));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumePreviewQuery.Template));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Blue")]
    [InlineData("navyblue")]
    public void Validate_InvalidAccent_Fails(string accent)
    {
        var result = _validator.Validate(Query(accent: accent));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumePreviewQuery.AccentColor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Sans")]
    [InlineData("modern")]
    public void Validate_InvalidFontPair_Fails(string font)
    {
        var result = _validator.Validate(Query(font: font));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumePreviewQuery.FontPair));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tight")]
    [InlineData("normal")]
    public void Validate_InvalidDensity_Fails(string density)
    {
        var result = _validator.Validate(Query(density: density));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumePreviewQuery.Density));
    }
}
