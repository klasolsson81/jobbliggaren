using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;

/// <summary>
/// Pre-handler defense-in-depth for <see cref="DisambiguateEmployersQuery"/> (ADR 0087 D6). The
/// company-name term must be 2–100 chars measured on the TRIMMED value (the handler trims before the
/// ILIKE), so an empty or all-whitespace term is rejected with a clean 400 rather than triggering a
/// near-whole-corpus scan. NO org.nr-format rule — the input is a NAME, not an org.nr (the whole
/// point of disambiguation is name→org.nr).
/// </summary>
public sealed class DisambiguateEmployersQueryValidator : AbstractValidator<DisambiguateEmployersQuery>
{
    public DisambiguateEmployersQueryValidator()
    {
        RuleFor(q => q.Query)
            .Must(q => q is not null && q.Trim().Length >= DisambiguateEmployersQuery.MinQueryLength)
                .WithMessage($"Sökordet måste vara minst {DisambiguateEmployersQuery.MinQueryLength} tecken.")
            .Must(q => q is null || q.Trim().Length <= DisambiguateEmployersQuery.MaxQueryLength)
                .WithMessage($"Sökordet får vara högst {DisambiguateEmployersQuery.MaxQueryLength} tecken.");
    }
}
