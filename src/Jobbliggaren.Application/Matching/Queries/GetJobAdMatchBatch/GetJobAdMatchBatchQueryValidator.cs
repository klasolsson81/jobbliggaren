using FluentValidation;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;

/// <summary>
/// F4-13 (ADR 0076 / ADR 0063) — batch-size guard enforced BEFORE the handler. The 100
/// cap (parity <c>GetJobAdStatusBatchQueryValidator</c> — the same /jobb page id set)
/// keeps the batch bounded so the single <c>= ANY</c> query is a DoS-floor, not an open
/// fan-out. A larger request → 400 (FluentValidation). Empty is allowed (→ empty result).
/// </summary>
public sealed class GetJobAdMatchBatchQueryValidator
    : AbstractValidator<GetJobAdMatchBatchQuery>
{
    public const int MaxJobAdIdsPerCall = 100;

    public GetJobAdMatchBatchQueryValidator()
    {
        RuleFor(q => q.JobAdIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids.Count <= MaxJobAdIdsPerCall)
            .WithMessage($"Max {MaxJobAdIdsPerCall} JobAd-IDs per anrop.");
    }
}
