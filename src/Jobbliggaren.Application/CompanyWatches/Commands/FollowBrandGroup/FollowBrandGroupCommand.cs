using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — follow a curated brand group by its slug for the current user. Returns
/// the <c>CompanyWatchId</c> (the surrogate resource; unfollow rides the same
/// <c>DELETE /company-watches/{id}</c> as an employer follow). Idempotent: re-following an already-active
/// group returns the existing id with no change; re-following a previously unfollowed group resurrects
/// the SAME row (FORK B1 parity). The slug is PUBLIC curated reference data — not PII — so, unlike
/// <c>FollowCompanyCommand</c>, it needs no redacting <c>ToString()</c>.
/// </summary>
public sealed record FollowBrandGroupCommand(string BrandGroupId)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "CompanyWatch.BrandGroupFollowed";
    public string AggregateType => "CompanyWatch";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
