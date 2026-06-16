using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.SavedSearches.Commands.ConfirmDerivedSearch;

/// <summary>
/// Input-shape validation for the CV→SavedSearch confirm command (Fas 4 STEG B). A confirmed
/// derived search MUST carry at least one confirmed ssyk-4 occupation group — that is the
/// distinguishing semantic from a manual filter (CreateSavedSearch). The per-id format/cap and
/// the full criteria invariants are enforced by <c>SearchCriteria.Create</c> in the handler.
/// </summary>
public sealed class ConfirmDerivedSearchCommandValidator : AbstractValidator<ConfirmDerivedSearchCommand>
{
    public ConfirmDerivedSearchCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Namn är obligatoriskt.")
            .MaximumLength(SavedSearch.NameMaxLength)
            .WithMessage($"Namn får vara max {SavedSearch.NameMaxLength} tecken.");

        RuleFor(c => c.OccupationGroup)
            .NotEmpty().WithMessage("Minst ett bekräftat yrke krävs för en CV-härledd sökning.");
    }
}
