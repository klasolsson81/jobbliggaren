using System.Text.Json;
using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;

/// <summary>
/// ADR 0080 Vag 4 PR-6 (Beslut 2) — sets the current user's background-match notification
/// consent: the opt-in toggle (<paramref name="Enabled"/>, GDPR Art. 6(1)(a)/7 — default OFF
/// per PR-1). The digest cadence is not part of it (<c>SetDigestCadenceCommand</c>). Owner-scoped.
/// The Domain method <see cref="JobSeeker.UpdateNotificationConsent"/> owns the consent stamping
/// (the first-ever opt-in is immutable Art. 7(1) evidence; an opt-out stamps the Art. 7(3)
/// withdrawal) — this command is a thin transport. NO AI/LLM, no PII (a bool).
/// <para>
/// <b>Required on the wire:</b> a body without <c>enabled</c> is a 400, never a bound
/// <c>false</c> that would record a withdrawal nobody made.
/// </para>
/// <para>
/// <b>Auditable (ADR 0022, GDPR Art. 5(2)/30):</b> a consent change is an accountability-relevant
/// event, so it carries <see cref="IAuditableCommand{TResponse}"/> — <c>AuditBehavior</c> writes one
/// <c>audit_log</c> row (actor + occurred-at + IP/UA + correlation) on success, parity the
/// owner-scoped <c>DeleteAccount</c> / <c>SetPrimaryResume</c> JobSeeker mutations. The aggregate is
/// the JobSeeker; the handler echoes its id via <see cref="Result{T}"/> so
/// <see cref="ExtractAggregateId"/> can read it (the command carries no id — it is owner-scoped on
/// the server-side UserId). The Art. 7(1)/7(3) EVIDENCE (the immutable consent-at / withdrawn-at)
/// lives on <c>Preferences</c>; the audit row is the who/when/where trail and carries the requested
/// <c>enabled</c>.
/// </para>
/// </summary>
public sealed record UpdateNotificationConsentCommand([property: JsonRequired] bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>,
      IAuditPayloadCommand<Result<Guid>>
{
    // Stable, append-only event name (audit queries depend on it). Provenance (2026-09-27): until
    // the digest cadence got its own command (JobSeeker.DigestCadenceUpdated), a cadence save was
    // written under this name too, and no row carried a payload. A row without a payload may
    // therefore be a cadence save; every row since carries the requested `enabled`.
    public string EventType => "JobSeeker.NotificationConsentUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;

    // The user's act as requested, not the resulting state: re-consent clears the withdrawal
    // timestamp, so Preferences alone cannot show every withdrawal.
    public string? BuildAuditPayload(Result<Guid> response, IIdentifierPseudonymizer pseudonymizer)
        => JsonSerializer.Serialize(new Dictionary<string, bool> { ["enabled"] = Enabled });
}
