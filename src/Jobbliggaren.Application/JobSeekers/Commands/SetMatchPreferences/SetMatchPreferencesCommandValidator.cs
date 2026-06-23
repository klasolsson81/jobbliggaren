using FluentValidation;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;

/// <summary>
/// Pre-handler defense-in-depth for <see cref="SetMatchPreferencesCommand"/> (F4-12),
/// mirroring <c>CreateSavedSearchCommandValidator</c>: per-list cap against the single
/// <see cref="SearchCriteria.MaxConceptIds"/> source + per-element concept-id pattern.
/// <b>Deliberate divergence:</b> there is NO "at least one criterion" rule — all lists
/// empty/null is valid (a user who has not stated preferences). The Domain factory
/// <c>MatchPreferences.Create</c> is the authoritative invariant source.
/// </summary>
public sealed class SetMatchPreferencesCommandValidator
    : AbstractValidator<SetMatchPreferencesCommand>
{
    // Mirrors SearchCriteria.ConceptIdPattern (defense-in-depth).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public SetMatchPreferencesCommandValidator()
    {
        RuleFor(c => c.PreferredOccupationGroups!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredOccupationGroups is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper.");

        RuleForEach(c => c.PreferredOccupationGroups)
            .Matches(ConceptIdPattern)
            .When(c => c.PreferredOccupationGroups is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.PreferredRegions!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredRegions is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner.");

        RuleForEach(c => c.PreferredRegions)
            .Matches(ConceptIdPattern)
            .When(c => c.PreferredRegions is not null)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.PreferredEmploymentTypes!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredEmploymentTypes is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer.");

        RuleForEach(c => c.PreferredEmploymentTypes)
            .Matches(ConceptIdPattern)
            .When(c => c.PreferredEmploymentTypes is not null)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(c => c.PreferredMunicipalities!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredMunicipalities is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner.");

        RuleForEach(c => c.PreferredMunicipalities)
            .Matches(ConceptIdPattern)
            .When(c => c.PreferredMunicipalities is not null)
            .WithMessage("Kommun måste vara en giltig JobTech municipality-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // ADR 0079 STEG 3 — confirmed skill concept-ids: same cap + per-element pattern
        // as the four dimensions above (defense-in-depth; MatchPreferences.Create is the
        // authoritative source).
        RuleFor(c => c.PreferredSkills!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredSkills is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kompetenser.");

        RuleForEach(c => c.PreferredSkills)
            .Matches(ConceptIdPattern)
            .When(c => c.PreferredSkills is not null)
            .WithMessage("Kompetens måste vara en giltig JobTech skill-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // ADR 0079 STEG 3 — stated years of experience: 0..MaxExperienceYears when
        // present (the single authoritative bound on MatchPreferences). Null = not
        // stated, valid.
        RuleFor(c => c.ExperienceYears!.Value)
            .InclusiveBetween(0, MatchPreferences.MaxExperienceYears)
            .When(c => c.ExperienceYears is not null)
            .WithMessage($"Antal års erfarenhet måste vara mellan 0 och {MatchPreferences.MaxExperienceYears}.");

        // ADR 0079-amendment (exp-per-occ PR-3) — the per-occupation experience overlay: same
        // cap as the concept-id lists, plus per-entry concept-id pattern + years range (the
        // dedicated child validator). The SUBSET rule (an entry only for an actually-preferred
        // group) is a cross-field domain invariant — left to MatchPreferences.Create (the
        // authoritative source). Null = honest empty, valid.
        RuleFor(c => c.PreferredOccupationExperience!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(c => c.PreferredOccupationExperience is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkeserfarenheter.");

        RuleForEach(c => c.PreferredOccupationExperience)
            .SetValidator(new OccupationExperienceInputValidator())
            .When(c => c.PreferredOccupationExperience is not null);
    }
}

/// <summary>
/// Per-entry defense-in-depth for the <see cref="OccupationExperienceInput"/> overlay (ADR
/// 0079-amendment): concept-id pattern + years range. <c>MatchPreferences.Create</c> remains the
/// authoritative invariant source (incl. the cross-entry subset/distinct rules).
/// </summary>
internal sealed class OccupationExperienceInputValidator : AbstractValidator<OccupationExperienceInput>
{
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public OccupationExperienceInputValidator()
    {
        RuleFor(e => e.ConceptId)
            .Matches(ConceptIdPattern)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(e => e.Years!.Value)
            .InclusiveBetween(0, MatchPreferences.MaxExperienceYears)
            .When(e => e.Years is not null)
            .WithMessage($"Antal års erfarenhet måste vara mellan 0 och {MatchPreferences.MaxExperienceYears}.");
    }
}
