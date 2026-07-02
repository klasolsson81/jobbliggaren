using FluentValidation;

namespace Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;

/// <summary>
/// Defense-in-depth pre-handler surface (ValidationBehavior). The JobAdId must be a non-empty Guid;
/// whether a follow-hit actually exists for it is resolved in the handler (a benign no-op there, never
/// a 400/404 — an absent hit must not be distinguishable from a present one).
/// </summary>
public sealed class MarkFollowedCompanyAdSeenCommandValidator : AbstractValidator<MarkFollowedCompanyAdSeenCommand>
{
    public MarkFollowedCompanyAdSeenCommandValidator()
    {
        RuleFor(c => c.JobAdId)
            .NotEmpty()
            .WithMessage("JobAdId krävs.");
    }
}
