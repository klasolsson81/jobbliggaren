using FluentValidation;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.JobSeekers.Commands;

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
