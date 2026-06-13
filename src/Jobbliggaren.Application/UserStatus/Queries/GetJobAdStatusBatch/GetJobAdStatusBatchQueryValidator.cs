using FluentValidation;

namespace Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch;

/// <summary>
/// ADR 0063 — batch-storleksgräns enforce:as FÖRE handlern.
/// 100 IDs är säker max för en list-page (typisk 20, headroom för future
/// virtualisering till 50+). Större request → 400 (FluentValidation).
/// </summary>
public sealed class GetJobAdStatusBatchQueryValidator
    : AbstractValidator<GetJobAdStatusBatchQuery>
{
    public const int MaxJobAdIdsPerCall = 100;

    public GetJobAdStatusBatchQueryValidator()
    {
        RuleFor(q => q.JobAdIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids.Count <= MaxJobAdIdsPerCall)
            .WithMessage($"Max {MaxJobAdIdsPerCall} JobAd-IDs per anrop.");
    }
}
