using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;

/// <summary>
/// Transport guard only (CTO BC-5) — fails a hostile or absurd payload fast, before it reaches the
/// domain. The VALUE rules (concept-id format, normalization, the empty-spec invariant) live in
/// <see cref="Domain.CompanyWatches.WatchFilterSpec"/> and are NOT duplicated here: two copies of a
/// rule is two places to drift.
///
/// <para>
/// The cap is per axis, reusing the same SSOT constant the VO does. An empty selection is VALID
/// here — it is how the user clears the filter (the handler maps it to <c>ClearFilter()</c>).
/// </para>
/// </summary>
public sealed class SetCompanyWatchFilterCommandValidator
    : AbstractValidator<SetCompanyWatchFilterCommand>
{
    public SetCompanyWatchFilterCommandValidator()
    {
        RuleFor(c => c.CompanyWatchId)
            .NotEmpty()
            .WithMessage("Bevakningen måste anges.");

        RuleFor(c => c.Municipalities)
            .NotNull()
            .WithMessage("Orter måste anges som en lista.")
            .Must(m => m.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per bevakningsfilter.");

        RuleFor(c => c.Regions)
            .NotNull()
            .WithMessage("Län måste anges som en lista.")
            .Must(r => r.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} län per bevakningsfilter.");
    }
}
