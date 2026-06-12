using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.SavedSearches.Commands.CreateSavedSearch;

public sealed class CreateSavedSearchCommandValidator
    : AbstractValidator<CreateSavedSearchCommand>
{
    // Speglar SearchCriteria.ConceptIdPattern + ListJobAdsQueryValidator
    // (defense-in-depth). Domän-faktorn är sanningskällan.
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public CreateSavedSearchCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .WithMessage("Namn är obligatoriskt.")
            .MaximumLength(SavedSearch.NameMaxLength)
            .WithMessage($"Namn får vara max {SavedSearch.NameMaxLength} tecken.");

        // Criteria-invarianter (minst ett kriterium, q-längd) ägs av
        // SearchCriteria.Create (Domain, sanningskälla) — handlern returnerar
        // dess DomainError som 400. SortBy + maxantal-cap/per-element-regex
        // valideras här som tidig defense-in-depth (paritet med
        // ListJobAdsQueryValidator; security-auditor M1 2026-05-16, §9.6
        // in-block-fix). Cap refererar Domain-konstanten (single source).
        // ADR 0067 Fas C2: OccupationGroup + Municipality ersätter Ssyk.
        RuleFor(c => c.SortBy).IsInEnum();

        RuleFor(c => c.OccupationGroup!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.OccupationGroup is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

        RuleForEach(c => c.OccupationGroup)
            .Matches(ConceptIdPattern)
            .When(c => c.OccupationGroup is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.Municipality!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.Municipality is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");

        RuleForEach(c => c.Municipality)
            .Matches(ConceptIdPattern)
            .When(c => c.Municipality is not null)
            .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.Region!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.Region is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");

        RuleForEach(c => c.Region)
            .Matches(ConceptIdPattern)
            .When(c => c.Region is not null)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 anställningsform + omfattning.
        RuleFor(c => c.EmploymentType!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.EmploymentType is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");

        RuleForEach(c => c.EmploymentType)
            .Matches(ConceptIdPattern)
            .When(c => c.EmploymentType is not null)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.WorktimeExtent!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.WorktimeExtent is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} omfattningar per sökning.");

        RuleForEach(c => c.WorktimeExtent)
            .Matches(ConceptIdPattern)
            .When(c => c.WorktimeExtent is not null)
            .WithMessage("Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");
    }
}
