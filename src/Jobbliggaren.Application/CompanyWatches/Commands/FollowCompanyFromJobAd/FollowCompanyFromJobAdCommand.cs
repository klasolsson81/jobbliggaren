using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompanyFromJobAd;

/// <summary>
/// #311 #455 (ADR 0087 D3/D8(c); senior-cto-advisor 2026-07-01) — follow an employer FROM a job ad. The
/// FE card-action keys the follow by <c>JobAdId</c> (non-PII); the handler resolves the ad's STORED
/// <c>organization_number</c> SERVER-SIDE and delegates to the same idempotent <c>CompanyWatch.Follow</c>
/// path as <c>FollowCompanyCommand</c>. The raw org.nr NEVER crosses the wire — a sole-prop org.nr can
/// equal a personnummer (CLAUDE.md §5), so Approach A keeps it entirely server-side.
///
/// <para>
/// Auditable (parity <c>FollowCompanyCommand</c>): a follow is an owner-scoped state transition. The
/// marker is opt-in — a command without it logs no audit row (memory
/// <c>reference_iauditablecommand_owner_scoped_audit</c>), so it is declared explicitly here.
/// </para>
/// </summary>
public sealed record FollowCompanyFromJobAdCommand(Guid JobAdId)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "CompanyWatch.Followed";

    public string AggregateType => "CompanyWatch";

    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
