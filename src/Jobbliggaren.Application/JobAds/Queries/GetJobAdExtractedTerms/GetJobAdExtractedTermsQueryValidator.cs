using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAdExtractedTerms;

/// <summary>
/// Garbage-floor enforced in the Validation pipeline before the handler
/// (mirrors <c>DeriveOccupationCodesQueryValidator</c>). <c>NotEmpty</c> rejects
/// <see cref="System.Guid.Empty"/> — no DB round-trip for an obviously invalid id.
/// </summary>
public sealed class GetJobAdExtractedTermsQueryValidator
    : AbstractValidator<GetJobAdExtractedTermsQuery>
{
    public GetJobAdExtractedTermsQueryValidator()
    {
        RuleFor(q => q.JobAdId)
            .NotEmpty().WithMessage("JobAdId krävs.");
    }
}
