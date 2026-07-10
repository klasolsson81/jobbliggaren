namespace Jobbliggaren.Application.Common.Abstractions;

public interface IAuthAuditLogger
{
    void LoginSucceeded(Guid userId, string sessionIdPrefix);
    void LoginFailed(string emailHash);

    /// <summary>
    /// A login/re-auth attempt hit a temporarily locked-out account (#503, OWASP A07).
    /// Distinct from <see cref="LoginFailed"/> so a burst from one emailHash/IP is a
    /// targeted-brute-force signal for TD-77-alarming — the wire response stays identical
    /// to a wrong-password 401 (oracle-avoidance), only the audit event differs.
    /// </summary>
    void AccountLockedOut(string emailHash);

    void LogoutSucceeded(Guid userId, string sessionIdPrefix);

    /// <summary>
    /// A registration email-confirmation link was RE-SENT for an unconfirmed account (#733). Emitted ONLY
    /// on the applicable branch (a resend actually happened) — an unknown/confirmed address is a non-event
    /// and writes no audit-log line, so the audit trail carries no account-existence signal. Aids
    /// email-bomb incident response (a burst for one userId is a targeted-abuse signal).
    /// </summary>
    void EmailConfirmationResent(Guid userId);
}
