using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.SavedSearches.Commands.UpdateSavedSearch;

public sealed class UpdateSavedSearchCommandValidator
    : AbstractValidator<UpdateSavedSearchCommand>
{
    // Speglar SearchCriteria.ConceptIdPattern + ListJobAdsQueryValidator
    // (defense-in-depth). Domän-faktorn är sanningskällan.
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public UpdateSavedSearchCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.Name)
            .NotEmpty()
            .WithMessage("Namn får inte vara tomt.")
            .MaximumLength(SavedSearch.NameMaxLength)
            .WithMessage($"Namn får vara max {SavedSearch.NameMaxLength} tecken.")
            .When(c => c.Name is not null);

        // Minst ett fält måste ändras — annars är PATCH:en en no-op som ändå
        // skulle skriva audit-rad (vilseledande granskningstrail).
        RuleFor(c => c)
            .Must(c => c.Name is not null || c.NotificationEnabled is not null || c.Criteria is not null)
            .WithMessage("Minst ett fält (namn, notisflagga eller kriterier) måste anges.");

        // Criteria-invarianter ägs av SearchCriteria.Create (Domain). SortBy +
        // maxantal-cap/per-element-regex valideras här som tidig
        // defense-in-depth när kriterier medskickas (paritet med
        // ListJobAdsQueryValidator; security-auditor M1 2026-05-16, §9.6
        // in-block-fix). Cap refererar Domain-konstanten (single source).
        // ADR 0067 Fas C2: OccupationGroup + Municipality ersätter Ssyk.
        When(c => c.Criteria is not null, () =>
        {
            RuleFor(c => c.Criteria!.SortBy).IsInEnum();

            RuleFor(c => c.Criteria!.OccupationGroup!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Criteria!.OccupationGroup is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

            RuleForEach(c => c.Criteria!.OccupationGroup)
                .Matches(ConceptIdPattern)
                .When(c => c.Criteria!.OccupationGroup is not null)
                .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

            RuleFor(c => c.Criteria!.Municipality!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Criteria!.Municipality is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");

            RuleForEach(c => c.Criteria!.Municipality)
                .Matches(ConceptIdPattern)
                .When(c => c.Criteria!.Municipality is not null)
                .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

            RuleFor(c => c.Criteria!.Region!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Criteria!.Region is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");

            RuleForEach(c => c.Criteria!.Region)
                .Matches(ConceptIdPattern)
                .When(c => c.Criteria!.Region is not null)
                .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

            // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
            RuleFor(c => c.Criteria!.EmploymentType!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Criteria!.EmploymentType is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");

            RuleForEach(c => c.Criteria!.EmploymentType)
                .Matches(ConceptIdPattern)
                .When(c => c.Criteria!.EmploymentType is not null)
                .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

            RuleFor(c => c.Criteria!.WorktimeExtent!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Criteria!.WorktimeExtent is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} omfattningar per sökning.");

            RuleForEach(c => c.Criteria!.WorktimeExtent)
                .Matches(ConceptIdPattern)
                .When(c => c.Criteria!.WorktimeExtent is not null)
                .WithMessage("Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");
        });
    }
}
