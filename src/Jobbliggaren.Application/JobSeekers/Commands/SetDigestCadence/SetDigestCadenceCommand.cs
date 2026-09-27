using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetDigestCadence;

/// <summary>
/// ADR 0087 D2 — sets the current user's digest cadence, the delivery rhythm the two notification
/// consents share and neither owns, so the command carries no consent value. Owner-scoped; the
/// aggregate's <see cref="JobSeeker.SetDigestCadence"/> refuses an undefined value. A body without
/// <c>cadence</c> is a 400, never a bound default.
/// <para>
/// <b>Auditable (ADR 0022, GDPR Art. 5(2)/30):</b> <c>AuditBehavior</c> writes one <c>audit_log</c>
/// row on success. The handler echoes the JobSeeker id via <see cref="Result{T}"/> so
/// <see cref="ExtractAggregateId"/> can read it.
/// </para>
/// </summary>
public sealed record SetDigestCadenceCommand([property: JsonRequired] DigestCadence Cadence)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    // Stable, append-only event name (audit queries depend on it).
    public string EventType => "JobSeeker.DigestCadenceUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
