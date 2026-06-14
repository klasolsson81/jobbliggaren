using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.DeriveOccupationCodes;

/// <summary>
/// DoS-/garbage-floor enforced in the Validation pipeline before the handler
/// (mirrors <c>SuggestJobAdTermsQueryValidator</c>). <c>Title</c> NotEmpty +
/// MaximumLength(100) — parity with the search-text cap (senior-cto-advisor
/// Decision 5). <c>NotEmpty</c> rejects null/empty/whitespace.
/// </summary>
public sealed class DeriveOccupationCodesQueryValidator
    : AbstractValidator<DeriveOccupationCodesQuery>
{
    public DeriveOccupationCodesQueryValidator()
    {
        RuleFor(q => q.Title)
            .NotEmpty().WithMessage("Yrkestitel krävs.")
            .MaximumLength(100).WithMessage("Yrkestitel får vara högst 100 tecken.");
    }
}
