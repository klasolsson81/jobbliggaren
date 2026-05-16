using FluentValidation;

namespace JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C — DoS-floor enforce:as i Validation-pipeline FÖRE
/// handlern (query körs aldrig med under-floor prefix). Speglar
/// ListJobAdsQueryValidator-mönstret.
/// </summary>
public sealed class SuggestJobAdTermsQueryValidator
    : AbstractValidator<SuggestJobAdTermsQuery>
{
    public SuggestJobAdTermsQueryValidator()
    {
        RuleFor(q => q.Prefix)
            .NotEmpty()
            .MinimumLength(2)      // ADR 0042 Beslut C — min prefix ≥2 (DoS-floor)
            .MaximumLength(100)    // speglar ListJobAdsQueryValidator.Q
            .WithMessage("Prefix måste vara 2-100 tecken.");

        RuleFor(q => q.Limit)
            .InclusiveBetween(1, 20)   // Take-cap mot response-DoS
            .WithMessage("Limit måste vara 1-20.");
    }
}
