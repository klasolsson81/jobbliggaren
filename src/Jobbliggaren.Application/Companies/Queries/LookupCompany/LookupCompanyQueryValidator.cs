using FluentValidation;

namespace Jobbliggaren.Application.Companies.Queries.LookupCompany;

/// <summary>
/// Pre-handler FORMAT gate for <see cref="LookupCompanyQuery"/>: exactly 10 digits (mirror
/// <c>OrganizationNumber.Create</c> / <c>FollowCompanyCommandValidator</c> — `^[0-9]{10}\z`, `\z` not
/// `$` against newline injection). The personnummer-shape POLICY deliberately does NOT live here
/// (ADR 0088 D4): policy belongs in the handler next to the masking/refusal semantics so the
/// refuse-copy and the D8(c) flag stay in one place, and a future #456-sanctioned posture flip is a
/// single-file change. The FE normalises (strips hyphen/spaces) before submitting; this is the
/// defense-in-depth 400 for anything else.
/// </summary>
public sealed class LookupCompanyQueryValidator : AbstractValidator<LookupCompanyQuery>
{
    public LookupCompanyQueryValidator()
    {
        RuleFor(q => q.OrganizationNumber)
            .Must(v => v is not null && System.Text.RegularExpressions.Regex.IsMatch(
                v, @"^[0-9]{10}\z"))
            .WithMessage("Organisationsnummer måste vara exakt 10 siffror.");
    }
}
