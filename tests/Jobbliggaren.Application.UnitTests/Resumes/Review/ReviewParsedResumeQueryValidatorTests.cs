using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4 STEG 9 (F4-9) — input-shape validation for the CV review query. Mirrors
/// <c>ImportResumeCommandValidator</c>: the ParsedResumeId must be a non-empty Guid and the
/// Profile string must parse fail-loud to a <c>RenderProfile</c> (a bad profile is a client
/// bug, never silently coerced to a default — parity with RubricVersion.Parse's fail-loud
/// discipline).
///
/// SPEC-DRIVEN. RED until ReviewParsedResumeQueryValidator ships.
/// </summary>
public class ReviewParsedResumeQueryValidatorTests
{
    private readonly ReviewParsedResumeQueryValidator _validator = new();

    private static ReviewParsedResumeQuery Query(
        Guid? id = null, string profile = "Ats") =>
        new(id ?? Guid.NewGuid(), profile);

    [Theory]
    [InlineData("Ats")]
    [InlineData("Visual")]
    public void Validate_ValidQuery_Passes(string profile)
    {
        var result = _validator.Validate(Query(profile: profile));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyParsedResumeId_Fails()
    {
        var result = _validator.Validate(Query(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ReviewParsedResumeQuery.ParsedResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ats")]        // case-sensitive: not an exact RenderProfile member
    [InlineData("Pdf")]        // not a RenderProfile member at all
    [InlineData("Both")]       // RubricProfile member, NOT a RenderProfile
    [InlineData("2")]          // numeric string: pre-fix Enum.TryParse accepted it (#478 Low)
    [InlineData(" Ats")]       // leading space: pre-fix Enum.TryParse TRIMMED then accepted (#478 Low)
    [InlineData("Ats ")]       // trailing space
    [InlineData(" 2 ")]        // whitespace-wrapped numeric: pre-fix trimmed to "2" then accepted
    [InlineData("+1")]         // signed numeric: pre-fix Enum.TryParse accepted leading sign
    [InlineData("Ats,Visual")] // comma flag-list: pre-fix OR-combined despite no [Flags]
    [InlineData("Ats, Visual")]
    [InlineData("0")]          // numeric mapping to a DEFINED member (Ats) — the insidious wrong-way-in
    [InlineData("1")]          // numeric mapping to Visual
    public void Validate_UnparseableProfile_Fails(string profile)
    {
        var result = _validator.Validate(Query(profile: profile));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ReviewParsedResumeQuery.Profile));
    }
}
