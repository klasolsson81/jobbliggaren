using FluentValidation;

namespace Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;

/// <summary>
/// F4-16 — guards the single-ad match detail query. A non-empty <c>JobAdId</c> is required
/// (an empty GUID is a malformed request → 400, never reaching the scorer). Mirrors the
/// style of <c>GetJobAdMatchBatchQueryValidator</c>.
/// </summary>
public sealed class GetJobAdMatchDetailQueryValidator
    : AbstractValidator<GetJobAdMatchDetailQuery>
{
    public GetJobAdMatchDetailQueryValidator()
    {
        RuleFor(q => q.JobAdId).NotEmpty();
    }
}
