using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// Admin operator action (#204 / TD-83 PR2): trigger an immediate ad-hoc run of a
/// recurring background job. <paramref name="RecurringJobId"/> is validated against
/// the closed <see cref="Jobbliggaren.Application.BackgroundJobs.RecurringJobIds"/>
/// allowlist (fan-out/RCE prevention — only known parameterless recurring jobs).
///
/// <para>
/// <b>Authorization:</b> <see cref="IAdminRequest"/> + endpoint
/// <c>RequireAuthorization(Admin)</c> + <c>AdminWritePolicy</c> rate-limit.
/// </para>
/// <para>
/// <b>Audit (Art. 30):</b> <see cref="IAuditableCommand{TResponse}"/> → one
/// audit_log row per request. AggregateId = per-request <see cref="RequestId"/>
/// (these system mutations have no aggregate-root Guid; <c>Guid.Empty</c> is forbidden
/// by <c>AuditLogEntry.Create</c>). The which-job-was-triggered detail belongs in the
/// audit Payload column, which is still unwired — <c>AuditLogEntry.Create</c> hard-codes
/// <c>payload: null</c>. (#842 found that the recruiter-PII command, whose precedent this
/// used to cite, documented a payload it never wrote. ADR 0106 PR3 wires the column.)
/// </para>
/// </summary>
public sealed record TriggerRecurringJobCommand(string RecurringJobId)
    : ICommand<Result<string>>, IAdminRequest, IAuditableCommand<Result<string>>
{
    /// <summary>Per-request Guid for the audit row, stable across the command lifetime.</summary>
    public Guid RequestId { get; } = Guid.NewGuid();

    public string EventType => "Admin.RecurringJobTriggered";
    public string AggregateType => "System.BackgroundJob";

    public Guid ExtractAggregateId(Result<string> response) => RequestId;
}
