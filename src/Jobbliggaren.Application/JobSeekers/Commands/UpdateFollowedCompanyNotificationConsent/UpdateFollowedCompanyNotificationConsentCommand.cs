using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateFollowedCompanyNotificationConsent;

/// <summary>
/// ADR 0087 D3/D5 (#311 PR-2b) — sets the current user's SEPARATE company-follow notification
/// consent: the opt-in toggle (<paramref name="Enabled"/>, GDPR Art. 6(1)(a)/7 — default OFF per
/// PR-4). A DISTINCT processing purpose from background-match notifications
/// (<see cref="UpdateNotificationConsent.UpdateNotificationConsentCommand"/>), so it carries its
/// OWN Art. 7(1)/7(3) evidence pair; the digest CADENCE is SHARED (ADR 0087 D2) and set via the
/// background-match consent command — this command deliberately carries ONLY the opt-in flag. The
/// Domain method <see cref="JobSeeker.UpdateFollowedCompanyNotificationConsent"/> (shipped in PR-4)
/// owns the consent stamping (the first-ever opt-in is immutable Art. 7(1) evidence; an opt-out
/// stamps the Art. 7(3) withdrawal) — this command is a thin transport. NO AI/LLM, no PII (a bool).
/// <para>
/// <b>Write-API deferral resolved (ADR 0087 D5-note, senior-cto-advisor 2026-07-01):</b> PR-4
/// shipped the domain method + persistence so the scan/dispatch can READ the flag; this command +
/// its endpoint are the deferred TOGGLE surface, landing here in PR-2b. Without it the shipped
/// follow-notification rail is unreachable — a user has no way to opt in.
/// </para>
/// <para>
/// <b>Auditable (ADR 0022, GDPR Art. 5(2)/30):</b> a consent change is accountability-relevant, so
/// it carries <see cref="IAuditableCommand{TResponse}"/> — <c>AuditBehavior</c> writes one
/// <c>audit_log</c> row (actor + occurred-at + IP/UA + correlation) on success, parity the
/// owner-scoped <c>UpdateNotificationConsent</c> mutation. The aggregate is the JobSeeker; the
/// handler echoes its id via <see cref="Result{T}"/> so <see cref="ExtractAggregateId"/> can read
/// it (the command carries no id — it is owner-scoped on the server-side UserId). The directional
/// Art. 7(1)/7(3) EVIDENCE (the immutable consent-at / withdrawn-at) lives on <c>Preferences</c>;
/// this audit row is the who/when/where trail.
/// </para>
/// <para>
/// No validator: <see cref="Enabled"/> needs no rule (any bool is valid — the Domain owns the
/// consent-stamping semantics), and there is no cadence field to range-check (cadence is shared,
/// set elsewhere). Mirrors the "Enabled needs no rule" note on the background-match command.
/// </para>
/// </summary>
public sealed record UpdateFollowedCompanyNotificationConsentCommand(bool Enabled)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    // Stable, append-only event name (audit queries depend on it). One event type for both
    // directions — the grant-vs-withdraw direction is recoverable from the Preferences
    // FollowedCompanyNotificationConsentAt / -WithdrawnAt timestamps (the authoritative Art. 7
    // evidence); the audit row is the action trail.
    public string EventType => "JobSeeker.FollowedCompanyNotificationConsentUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
