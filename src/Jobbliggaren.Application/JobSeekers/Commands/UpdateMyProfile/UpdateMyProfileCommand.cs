using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;

/// <summary>
/// TD-115 (2026-06-25): the legacy EmailNotifications/WeeklySummary flags were retired
/// (they gated no email path — see Preferences) so this command carries only DisplayName + locale.
/// <para>
/// <b>Auditable (#192, GDPR Art. 5(2)/30):</b> a profile mutation is accountability-relevant —
/// <c>DisplayName</c> is personal data — so this owner-scoped JobSeeker mutation carries
/// <see cref="IAuditableCommand{TResponse}"/>; <c>AuditBehavior</c> writes ONE <c>audit_log</c> row
/// (actor + occurred-at + IP/UA + correlation) on success, closing the audit-coverage gap left by
/// every OTHER owner-scoped JobSeeker self-mutation already being audited (UpdateNotificationConsent
/// / SetPrimaryResume / DeleteAccount). The command carries no id (owner-scoped on the server-side
/// UserId); the handler echoes the JobSeeker id via <see cref="Result{T}"/> so
/// <see cref="ExtractAggregateId"/> can read it. The endpoint discards the echoed id (still 200).
/// </para>
/// </summary>
public sealed record UpdateMyProfileCommand(
    string? DisplayName,
    string? Language)
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    // Stable, append-only event name (audit queries depend on it). One event covers the
    // profile mutation (DisplayName and/or locale) — parity JobSeeker.NotificationConsentUpdated.
    public string EventType => "JobSeeker.ProfileUpdated";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
