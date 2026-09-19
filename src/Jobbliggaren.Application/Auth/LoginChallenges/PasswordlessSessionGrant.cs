using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>How a session was earned: the login-succeeded audit event records it.</summary>
public enum LoginMethod
{
    Password,
    Code,
    Link,
}

/// <summary>Whether proving the inbox changed the account.</summary>
public enum InboxProof
{
    /// <summary>The address was already confirmed: nothing was written.</summary>
    AlreadyConfirmed,

    /// <summary>
    /// The address was unconfirmed: it is now confirmed, the password is removed and the security stamp is
    /// rotated, in one Identity write.
    /// </summary>
    FirstProofRecorded,
}

/// <summary>
/// Records a first passwordless proof of an account's inbox (#1735, security-auditor Q21/Q-S3). An account
/// whose address was never confirmed may hold a password set by someone who registered the address before
/// its owner did; confirming the address and leaving that password would let it in. So the confirmation,
/// the password's removal and the stamp rotation are ONE write. Reachable only from
/// <see cref="PasswordlessSessionGrant"/>, after a verified or consumed challenge — never a bare
/// force-confirm (ADR 0127 refused exactly that); a type-level test pins the single consumer.
/// </summary>
public interface IInboxProofRecorder
{
    Task<InboxProof> RecordAsync(Guid userId, CancellationToken ct);
}

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
            // A credential changed, so it is an audit_log row, not an ops line: a completed credential change
            // on a known user id is auditable (IAuthAuditLogger's rule), and the row is written after proof,
            // so it says nothing about whether any other address has an account (security-auditor Q-S3).
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow,
                correlationId: correlationId.Current,
                userId: subject.UserId,
                eventType: InboxProvenAuditEventType,
                aggregateType: "User",
                aggregateId: subject.UserId,
                ipAddress: requestContext.IpAddress,
                userAgent: requestContext.UserAgent));

            // The Identity write has committed, so from here on CancellationToken.None: a session opened with
            // the removed password is revoked BEFORE the new one exists (the change-password ordering).
            await sessions.InvalidateAllForUserAsync(subject.UserId, CancellationToken.None);
        }

        var session = await sessions.CreateAsync(subject.UserId, SessionLifetime.Persistent, CancellationToken.None);
        audit.LoginSucceeded(subject.UserId, session.Id.ToString(), method);
        return new SessionDto(session.Id.Reveal());
    }
}

/// <summary>What a proven login challenge leads to. A closed set: only the variants nested here exist.</summary>
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    public sealed record SignedIn(string SessionId) : LoginOutcome
    {
        public const string WireName = "signedIn";
    }

    public sealed record PendingDeletion(DateOnly PermanentDeletionEarliest) : LoginOutcome
    {
        public const string WireName = "pendingDeletion";
    }

    public sealed record RegistrationClosed : LoginOutcome
    {
        public const string WireName = "registrationClosed";
    }
}

/// <summary>
/// The one function both proofs — a code and a link — end in (senior-cto-advisor Q1, 2026-09-19). It
/// resolves the proven address at proof time, not at issue time, so an account deleted or a kill-switch
/// thrown inside the challenge's 15 minutes is honoured.
/// </summary>
public sealed class LoginProofOutcome(LoginSubjectResolver subjects, PasswordlessSessionGrant grant)
{
    public async Task<LoginOutcome> ResolveAsync(LoginChallengeProof proof, LoginMethod method, CancellationToken ct) =>
        await subjects.ResolveAsync(proof.ProvenEmail, ct) switch
        {
            LoginSubject.Active active =>
                new LoginOutcome.SignedIn((await grant.GrantAsync(active, method, ct)).SessionId),
            LoginSubject.PendingDeletion pending =>
                new LoginOutcome.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),
            _ => new LoginOutcome.RegistrationClosed(),
        };
}

/// <summary>The earliest date an account in its restore window can be removed.</summary>
public static class AccountRestoreWindow
{
    // The hard-delete job's cutoff is the restore window before "now", so a row soft-deleted at T is removed
    // at the first run after T + window. The UTC date of T + window is on or before that run, which is what
    // "tidigast" promises.
    public static DateOnly PermanentDeletionEarliest(DateTimeOffset deletedAt) =>
        DateOnly.FromDateTime(deletedAt.AddDays(HardDeleteAccountsJob.RestoreWindowDays).UtcDateTime);
}
