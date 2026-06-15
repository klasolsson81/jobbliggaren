using Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG 10 (F4-10) — input-shape validation for the CV-improve query. Mirrors
/// <c>ReviewParsedResumeQueryValidator</c>: the ParsedResumeId must be a non-empty Guid and the
/// Profile string must parse fail-loud to a <c>RenderProfile</c> (a bad profile is a client bug,
/// never silently coerced — parity RubricVersion.Parse's fail-loud discipline).
/// </summary>
public class SuggestCvImprovementsQueryValidatorTests
{
    private readonly SuggestCvImprovementsQueryValidator _validator = new();

    private static SuggestCvImprovementsQuery Query(Guid? id = null, string profile = "Ats") =>
        new(id ?? Guid.NewGuid(), profile);

    [Theory]
    [InlineData("Ats")]
    [InlineData("Visual")]
    public void Validate_ValidQuery_Passes(string profile)
    {
        _validator.Validate(Query(profile: profile)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyParsedResumeId_Fails()
    {
        var result = _validator.Validate(Query(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SuggestCvImprovementsQuery.ParsedResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ats")]        // case-sensitive: not an exact RenderProfile member
    [InlineData("Pdf")]        // not a RenderProfile member at all
    [InlineData("Both")]       // RubricProfile member, NOT a RenderProfile
    public void Validate_UnparseableProfile_Fails(string profile)
    {
        var result = _validator.Validate(Query(profile: profile));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SuggestCvImprovementsQuery.Profile));
    }
}
