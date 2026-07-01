using FluentValidation;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;

/// <summary>
/// #455 (ADR 0063 batch-precedent) — batch size enforced BEFORE the handler. 100 ids is the safe max
/// for a list page (typical 20, headroom for future virtualisation). Larger request → 400.
/// </summary>
public sealed class GetCompanyWatchStatusBatchQueryValidator
    : AbstractValidator<GetCompanyWatchStatusBatchQuery>
{
    public const int MaxJobAdIdsPerCall = 100;

    public GetCompanyWatchStatusBatchQueryValidator()
    {
        RuleFor(q => q.JobAdIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids.Count <= MaxJobAdIdsPerCall)
            .WithMessage($"Max {MaxJobAdIdsPerCall} JobAd-IDs per anrop.");
    }
}
