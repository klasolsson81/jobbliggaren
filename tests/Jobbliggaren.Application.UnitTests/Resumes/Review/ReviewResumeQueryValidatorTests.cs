using Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-4 (#653) — input-shape validation for the canonical CV review query. Parity with
/// <c>ReviewParsedResumeQueryValidator</c>: the ResumeId must be a non-empty Guid and the Profile
/// string must parse fail-loud to a <c>RenderProfile</c> (a bad profile is a client bug, never
/// silently coerced to a default — the RenderProfileNames case-sensitive contract).
/// </summary>
public class ReviewResumeQueryValidatorTests
{
    private readonly ReviewResumeQueryValidator _validator = new();

    private static ReviewResumeQuery Query(Guid? id = null, string profile = "Ats") =>
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
    public void Validate_EmptyResumeId_Fails()
    {
        var result = _validator.Validate(Query(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ReviewResumeQuery.ResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ats")]        // case-sensitive: not an exact RenderProfile member
    [InlineData("Pdf")]        // not a RenderProfile member at all
    [InlineData("Both")]       // RubricProfile member, NOT a RenderProfile
    [InlineData("2")]          // numeric string
    [InlineData(" Ats")]       // leading space
    [InlineData("Ats ")]       // trailing space
    [InlineData("0")]          // numeric mapping to a defined member (Ats) — the wrong-way-in
    [InlineData("1")]          // numeric mapping to Visual
    public void Validate_UnparseableProfile_Fails(string profile)
    {
        var result = _validator.Validate(Query(profile: profile));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ReviewResumeQuery.Profile));
    }
}
