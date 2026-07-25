using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C — DoS-floor enforce:as i Validation-pipeline FÖRE
/// handlern (query körs aldrig med under-floor prefix).
/// <para><b>#831 — ta INTE bort <c>MinimumLength(2)</c> här som del av en
/// konsistens-svepning.</b> Den mirroring mot <c>ListJobAdsQueryValidator</c> som
/// stod här gällde min+max; sedan #831 delas bara MAX. Skillnaden är inte
/// slarv utan mekanik: list-/facet-/remote-count-vägarna kör sitt <c>q</c> genom
/// <c>ISearchQueryParser</c>, som NOLLAR en residual under minimum, så där är
/// validatorns 400 den andra av två regler. Den här vägen har INGEN parser —
/// <c>SuggestJobAdTermsQueryHandler</c> bygger
/// <c>LikePattern.EscapePrefix(Prefix) + "%"</c>, alltså en LIKE-prefix-skanning
/// direkt på användarens tecken. Här är minimum ENDA vakten, och ett 1-tecken-
/// prefix (<c>a%</c>) är precis den skanning golvet finns för.</para>
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
