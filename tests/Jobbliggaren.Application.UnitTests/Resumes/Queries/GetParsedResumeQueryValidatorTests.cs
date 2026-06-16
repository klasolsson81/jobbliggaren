using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>Fas 4 STEG B / B1b — input-shape validation for GetParsedResumeQuery
/// (parity ReviewParsedResumeQueryValidatorTests; the coverage gate, ADR 0044, requires
/// validation tests for new queries).</summary>
public class GetParsedResumeQueryValidatorTests
{
    private readonly GetParsedResumeQueryValidator _validator = new();

    [Fact]
    public void Validate_NonEmptyId_Passes()
    {
        var result = _validator.Validate(new GetParsedResumeQuery(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = _validator.Validate(new GetParsedResumeQuery(Guid.Empty));
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetParsedResumeQuery.ParsedResumeId));
    }
}
