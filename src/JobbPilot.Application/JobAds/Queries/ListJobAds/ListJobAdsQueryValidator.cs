using FluentValidation;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryValidator : AbstractValidator<ListJobAdsQuery>
{
    // JobTech v2 concept-id-format: kort sträng, alfanumerisk + `_-`, observerade
    // exempel ~12 tecken (`MVqp_eS8_kDZ`). Sätter 1-32 som defense-in-depth-yta
    // (Saltzer/Schroeder 1975 default-deny). CTO-rond 2026-05-13 Q7a/Q7b.
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}$";

    public ListJobAdsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.SortBy).IsInEnum();

        RuleFor(q => q.Ssyk)
            .Matches(ConceptIdPattern)
            .When(q => !string.IsNullOrWhiteSpace(q.Ssyk))
            .WithMessage("Ssyk måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Region)
            .Matches(ConceptIdPattern)
            .When(q => !string.IsNullOrWhiteSpace(q.Region))
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // q MinLength(2) hindrar `?q=a` (matchar närapå hela tabellen → DoS-yta).
        // MaxLength(100) räcker för normal söksträng + safety margin mot injection-
        // stuffing. CTO-rond 2026-05-13 Q7c.
        RuleFor(q => q.Q)
            .MinimumLength(2)
            .MaximumLength(100)
            .When(q => !string.IsNullOrWhiteSpace(q.Q))
            .WithMessage("Söktext måste vara 2-100 tecken.");
    }
}
