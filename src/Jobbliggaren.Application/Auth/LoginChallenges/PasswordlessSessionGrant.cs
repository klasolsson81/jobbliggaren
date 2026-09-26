using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one grant of a passwordless session (ADR 0142 D3/D4). It takes an <see cref="LoginSubject.Active"/>
/// only, so the #1349 rule — no session for a soft-deleted or missing profile — holds by type. Every
/// session it creates is <see cref="SessionLifetime.Persistent"/>.
/// </summary>
public sealed class PasswordlessSessionGrant(
    IInboxProofRecorder inboxProof,
    ISessionStore sessions,
    IAuthAuditLogger audit,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext)
{
    /// <summary>The <c>audit_log</c> event for a first passwordless proof of an unconfirmed address.</summary>
    public const string InboxProvenAuditEventType = "User.InboxProvenByLogin";

    public async Task<SessionDto> GrantAsync(LoginSubject.Active subject, LoginMethod method, CancellationToken ct)
    {
        var proof = await inboxProof.RecordAsync(subject.UserId, ct);

        if (proof == InboxProof.FirstProofRecorded)
        {
            // The address is confirmed and every earlier session is revoked, a change to the account's security
            // state on a known user id, so it is an audit_log row, not an ops line; the row is written after
            // proof, so it says nothing about whether any other address has an account (security-auditor Q-S3).
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow,
                correlationId: correlationId.Current,
                userId: subject.UserId,
                eventType: InboxProvenAuditEventType,
                aggregateType: "User",
                aggregateId: subject.UserId,
                ipAddress: requestContext.IpAddress,
                userAgent: requestContext.UserAgent));

            // The Identity write has committed, so from here on CancellationToken.None: every earlier session is
            // revoked BEFORE the new one exists.
            await db.SaveChangesAsync(CancellationToken.None);
            await sessions.InvalidateAllForUserAsync(subject.UserId, CancellationToken.None);
        }

        var session = await sessions.CreateAsync(subject.UserId, SessionLifetime.Persistent, CancellationToken.None);
        audit.LoginSucceeded(subject.UserId, session.Id.ToString(), method);
        return new SessionDto(session.Id.Reveal());
    }
}
