using FluentValidation;

namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;

/// <summary>
/// #446 — batch-size guard enforced BEFORE the handler (parity <c>GetJobAdStatusBatchQueryValidator</c>,
/// ADR 0063). 100 IDs is the safe max for one /jobb list page (typical 20, headroom for future
/// virtualisation); a larger request → 400 (FluentValidation). Keeps the overlay bounded so the
/// per-page round-trip stays off the N+1 path (ADR 0045 / CLAUDE.md §2.5).
/// </summary>
public sealed class GetEmployerApplicationCountBatchQueryValidator
    : AbstractValidator<GetEmployerApplicationCountBatchQuery>
{
    public const int MaxJobAdIdsPerCall = 100;

    public GetEmployerApplicationCountBatchQueryValidator()
    {
        RuleFor(q => q.JobAdIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids.Count <= MaxJobAdIdsPerCall)
            .WithMessage($"Max {MaxJobAdIdsPerCall} JobAd-IDs per anrop.");
    }
}
