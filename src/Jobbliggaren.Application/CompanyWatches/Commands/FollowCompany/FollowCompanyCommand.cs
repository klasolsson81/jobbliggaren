using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — follow an employer by org.nr for the current user. Returns the
/// <c>CompanyWatchId</c> (FORK D2 surrogate resource). Idempotent: re-following an already-active
/// org.nr returns the existing id with no change; re-following a previously unfollowed org.nr
/// resurrects the SAME row (FORK B1). The org.nr travels in the request BODY (never a URL/log per
/// D8(c)); <c>LoggingBehavior</c> logs the message type name only.
/// </summary>
public sealed record FollowCompanyCommand(string OrganizationNumber)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "CompanyWatch.Followed";
    public string AggregateType => "CompanyWatch";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    /// <summary>
    /// REDACTED (#883). The org.nr is client-supplied input and IS the entire payload (the doc above
    /// says it "travels in the request BODY, never a URL/log"); a record's compiler-generated
    /// <c>ToString()</c> would still write it into a log for a plain <c>{X}</c> MEL placeholder, and it
    /// can be a sole-prop personnummer (ADR 0087 D8(c); CLAUDE.md §5). This makes "never a log"
    /// structural rather than convention-dependent; pinned by <c>OrgNrRecordLoggingGuardTests</c>.
    /// </summary>
    public override string ToString() => "FollowCompanyCommand(org.nr redacted)";
}
