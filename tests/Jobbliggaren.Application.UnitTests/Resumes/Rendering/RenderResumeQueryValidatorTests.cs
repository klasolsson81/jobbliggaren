using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// TD-112 / #202 — input-shape validation for the render-by-Resume-id query. Mirrors
/// <c>RenderCvQueryValidator</c>: the ResumeId must be a non-empty Guid and the Profile string
/// must parse fail-loud to a <c>RenderProfile</c> (no silent default).
/// </summary>
public class RenderResumeQueryValidatorTests
{
    private readonly RenderResumeQueryValidator _validator = new();

    private static RenderResumeQuery Query(Guid? id = null, string profile = "Ats") =>
        new(id ?? Guid.NewGuid(), profile);

    [Theory]
    [InlineData("Ats")]
    [InlineData("Visual")]
    public void Validate_ValidQuery_Passes(string profile)
    {
        _validator.Validate(Query(profile: profile)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyResumeId_Fails()
    {
        var result = _validator.Validate(Query(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumeQuery.ResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ats")]
    [InlineData("Pdf")]
    [InlineData("Both")]
    public void Validate_UnparseableProfile_Fails(string profile)
    {
        var result = _validator.Validate(Query(profile: profile));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RenderResumeQuery.Profile));
    }
}
