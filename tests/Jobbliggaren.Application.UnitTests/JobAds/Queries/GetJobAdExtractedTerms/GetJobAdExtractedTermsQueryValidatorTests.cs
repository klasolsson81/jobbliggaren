using Jobbliggaren.Application.JobAds.Queries.GetJobAdExtractedTerms;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetJobAdExtractedTerms;

// Fas 4 STEG 4 (F4-4) — garbage-floor enforced in the Validation pipeline before
// the handler (mirrors DeriveOccupationCodesQueryValidator): NotEmpty JobAdId
// rejects Guid.Empty so there is no DB round-trip for an obviously invalid id.
//
// RED until GetJobAdExtractedTermsQuery + GetJobAdExtractedTermsQueryValidator ship.
public class GetJobAdExtractedTermsQueryValidatorTests
{
    private readonly GetJobAdExtractedTermsQueryValidator _validator = new();

    [Fact]
    public void Validate_EmptyGuid_IsInvalid()
    {
        var result = _validator.Validate(new GetJobAdExtractedTermsQuery(Guid.Empty));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_NonEmptyGuid_Passes()
    {
        var result = _validator.Validate(new GetJobAdExtractedTermsQuery(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }
}
