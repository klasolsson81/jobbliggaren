using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// Admin operator action (#204 / TD-83 PR2): requeue a failed background job. The
/// handler requeues only a job that currently exists and is in the Failed state
/// (a missing job → NotFound/404, a non-Failed job → Conflict/409), so a rejected
/// requeue never produces a false "succeeded" audit row.
///
/// <para>
/// <b>Authorization:</b> <see cref="IAdminRequest"/> + endpoint
/// <c>RequireAuthorization(Admin)</c> + <c>AdminWritePolicy</c> rate-limit.
/// </para>
/// <para>
/// <b>Audit (Art. 30):</b> <see cref="IAuditableCommand{TResponse}"/> → one
/// audit_log row per successful requeue (AuditBehavior skips on failure).
/// AggregateId = per-request <see cref="RequestId"/> (<c>Guid.Empty</c> is forbidden
/// by <c>AuditLogEntry.Create</c>; the per-request-Guid convention for system events
/// with no aggregate root originated in the recruiter-PII erasure command, which was
/// removed in #842 — the convention outlives it).
/// </para>
/// </summary>
public sealed record RequeueFailedJobCommand(string JobId)
    : ICommand<Result<bool>>, IAdminRequest, IAuditableCommand<Result<bool>>
{
    /// <summary>Per-request Guid for the audit row, stable across the command lifetime.</summary>
    public Guid RequestId { get; } = Guid.NewGuid();

    public string EventType => "Admin.FailedJobRequeued";
    public string AggregateType => "System.BackgroundJob";

    public Guid ExtractAggregateId(Result<bool> response) => RequestId;
}
