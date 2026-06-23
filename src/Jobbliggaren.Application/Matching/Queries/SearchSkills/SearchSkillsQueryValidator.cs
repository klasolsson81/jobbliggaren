using FluentValidation;

namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// Input-shape guard for the skill typeahead (ADR 0079 STEG 3 PR-C). Deliberately
/// lenient: no NotEmpty / min-length rule — a blank or too-short query is a valid
/// typeahead state that returns an empty list (the resolver enforces the min length),
/// never a 400 mid-typing. Only a MaximumLength DoS bound (a typeahead query is short).
/// </summary>
public sealed class SearchSkillsQueryValidator : AbstractValidator<SearchSkillsQuery>
{
    public SearchSkillsQueryValidator()
    {
        RuleFor(q => q.Query)
            .MaximumLength(80)
            .WithMessage("Sökfrågan får vara max 80 tecken.");
    }
}
