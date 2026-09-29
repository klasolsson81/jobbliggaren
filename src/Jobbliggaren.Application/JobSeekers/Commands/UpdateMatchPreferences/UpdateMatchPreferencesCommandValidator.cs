using System.Linq.Expressions;
using FluentValidation;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;

/// <summary>
/// Pre-handler defense-in-depth for <see cref="UpdateMatchPreferencesCommand"/>: the per-list
/// cap and concept-id pattern, applied to the parts that are present.
/// <c>MatchPreferences.Create</c> stays the authoritative invariant source.
/// </summary>
public sealed class UpdateMatchPreferencesCommandValidator
    : AbstractValidator<UpdateMatchPreferencesCommand>
{
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public UpdateMatchPreferencesCommandValidator()
    {
        RuleFor(c => c)
            .Must(c => c.Occupations is not null || c.Skills is not null || c.Locations is not null
                || c.EmploymentTypes is not null || c.Experience is not null)
            .WithMessage("Minst en del (yrken, kompetenser, orter, anställningsformer eller erfarenhet) måste anges.");

        When(c => c.Occupations is not null, () =>
        {
            ConceptList(c => c.Occupations!.PreferredOccupationGroups,
                "yrkesgrupper",
                "Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

            RuleFor(c => c.Occupations!.PreferredOccupationExperience!)
                .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
                .When(c => c.Occupations!.PreferredOccupationExperience is not null)
                .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkeserfarenheter.");

            RuleForEach(c => c.Occupations!.PreferredOccupationExperience)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .SetValidator(new OccupationExperienceInputValidator())
                .When(c => c.Occupations!.PreferredOccupationExperience is not null);
        });

        When(c => c.Skills is not null, () =>
            ConceptList(c => c.Skills!.PreferredSkills,
                "kompetenser",
                "Kompetens måste vara en giltig JobTech skill-concept-id (1-32 tecken, alfanumeriskt + _-)."));

        When(c => c.Locations is not null, () =>
        {
            ConceptList(c => c.Locations!.PreferredRegions,
                "regioner",
                "Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");
            ConceptList(c => c.Locations!.PreferredMunicipalities,
                "kommuner",
                "Kommun måste vara en giltig JobTech municipality-concept-id (1-32 tecken, alfanumeriskt + _-).");
        });

        When(c => c.EmploymentTypes is not null, () =>
            ConceptList(c => c.EmploymentTypes!.PreferredEmploymentTypes,
                "anställningsformer",
                "Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-)."));

        RuleFor(c => c.Experience!.ExperienceYears!.Value)
            .InclusiveBetween(0, MatchPreferences.MaxExperienceYears)
            .When(c => c.Experience?.ExperienceYears is not null)
            .WithMessage($"Antal års erfarenhet måste vara mellan 0 och {MatchPreferences.MaxExperienceYears}.");
    }

    // A present part's list is required: an explicit JSON null passes JsonRequired, and [] is the
    // only way to clear.
    private void ConceptList(
        Expression<Func<UpdateMatchPreferencesCommand, IReadOnlyList<string>>> list,
        string plural,
        string invalidMessage)
    {
        RuleFor(list)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage($"Listan med {plural} måste anges; en tom lista rensar.")
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} {plural}.")
            .ForEach(item => item.Matches(ConceptIdPattern).WithMessage(invalidMessage));
    }
}
