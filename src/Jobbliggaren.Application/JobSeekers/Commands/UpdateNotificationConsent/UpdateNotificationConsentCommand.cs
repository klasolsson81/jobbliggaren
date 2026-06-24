using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;

/// <summary>
/// ADR 0080 Vag 4 PR-6 (Beslut 2) — sets the current user's background-match notification
/// consent: the opt-in toggle (<paramref name="Enabled"/>, GDPR Art. 6(1)(a)/7 — default OFF
/// per PR-1) and the digest cadence for accumulated Strong matches (Top is always direct, Good
/// stays in-app — ADR 0080 Beslut 6). Owner-scoped. The Domain method
/// <see cref="JobSeeker.UpdateNotificationConsent"/> owns the consent stamping (the first-ever
/// opt-in is immutable Art. 7(1) evidence; an opt-out stamps the Art. 7(3) withdrawal) — this
/// command is a thin transport. NO AI/LLM, no PII (a bool + an enum).
/// <para>
/// <b>Auditable (ADR 0022, GDPR Art. 5(2)/30):</b> a consent change is an accountability-relevant
/// event, so it carries <see cref="IAuditableCommand{TResponse}"/> — <c>AuditBehavior</c> writes one
/// <c>audit_log</c> row (actor + occurred-at + IP/UA + correlation) on success, parity the
/// owner-scoped <c>DeleteAccount</c> / <c>SetPrimaryResume</c> JobSeeker mutations. The aggregate is
/// the JobSeeker; the handler echoes its id via <see cref="Result{T}"/> so
/// <see cref="ExtractAggregateId"/> can read it (the command carries no id — it is owner-scoped on
/// the server-side UserId). The directional Art. 7(1)/7(3) EVIDENCE (the immutable consent-at /
/// withdrawn-at) lives on <c>Preferences</c>; this audit row is the who/when/where trail.
/// </para>
/// </summary>
public sealed record UpdateNotificationConsentCommand(bool Enabled, DigestCadence Cadence)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    // Stable, append-only event name (audit queries depend on it). One event type for both
    // directions — the grant-vs-withdraw direction is recoverable from the Preferences consent-at /
    // withdrawn-at timestamps (the authoritative Art. 7 evidence); the audit row is the action trail.
    public string EventType => "JobSeeker.NotificationConsentUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
