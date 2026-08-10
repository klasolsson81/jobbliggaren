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

    /// <summary>
    /// A password-reset link was sent (#1171). Emitted ONLY on the branch where a link actually went out
    /// — an unknown address is a non-event and writes nothing, so the log carries no account-existence
    /// signal. Aids email-bomb incident response the same way <see cref="EmailConfirmationResent"/> does.
    /// <para>
    /// <b>Why this is an ops log line and not an <c>IAuditableCommand</c>, which is where it would
    /// otherwise belong.</b> <c>AuditBehavior</c> stamps a row on <c>Result.Success</c>, and the request
    /// handler returns success for EVERY well-formed address. An auditable command also needs an
    /// aggregate id, and the only id available exists solely for a real account — so <c>audit_log</c>
    /// would gain a row for existing addresses and none for unknown ones, turning the audit table itself
    /// into an enumeration oracle for anyone who can read it. The completed reset IS auditable; the
    /// request is not. Same reasoning as <see cref="EmailConfirmationResent"/>.
    /// </para>
    /// </summary>
    /// <summary>
    /// #1171 — a password-reset link was sent. Written from OUTSIDE a request scope, with the client
    /// context carried IN rather than read from an <c>HttpContext</c> that no longer exists.
    /// <para>
    /// Needed because the send moved off the request path: the dispatch consumer is a background
    /// service, so the implementation's <c>IHttpContextAccessor</c> read returns null there and both
    /// fields would silently degrade to "unknown" — on the auth event most closely tied to account
    /// takeover, and exactly the defence-in-depth ADR 0024 D7 ratified. The values are anonymised and
    /// truncated by the request path before they are carried, so this overload never receives a raw
    /// address or an untruncated agent.
    /// </para>
    /// </summary>
    void PasswordResetRequested(Guid userId, string? ipAddress, string? userAgent);
}
