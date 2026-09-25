namespace Jobbliggaren.Application.Common.Abstractions;

public interface IAuthAuditLogger
{
    /// <summary>A session was granted; <paramref name="method"/> records how it was earned (#1735).</summary>
    void LoginSucceeded(Guid userId, string sessionIdPrefix, Auth.LoginMethod method);

    void LogoutSucceeded(Guid userId, string sessionIdPrefix);

    /// <summary>
    /// #1735 — a login-challenge mail was sent for a known account (ADR 0142 D3). Written from the dispatch
    /// consumer, so the client context is carried in rather than read from an <c>HttpContext</c> that does not
    /// exist there.
    /// <see cref="Auth.LoginChallenges.LoginChallengeKind.LinkOnly"/> is the operational signal of the
    /// third-party budget drain Klas's option (A) answers.
    /// </summary>
    void LoginChallengeIssued(
        Guid userId,
        Auth.LoginChallenges.LoginChallengeKind challengeKind,
        string? ipAddress,
        string? userAgent);

    /// <summary>
    /// #1739 — a re-authentication grant was redeemed for a sensitive operation (ADR 0142 D5). Written from
    /// <c>ReauthenticationService</c>, never from the behavior. Never an address, a code or a token.
    /// </summary>
    void ReauthenticationSucceeded(Guid userId, Auth.Grants.GrantPurpose purpose);

    /// <summary>
    /// #1739 — a re-authentication was refused: no grant, a grant that could not be redeemed, or a soft-deleted
    /// account. After 5b this is the only brake on a hijacked 180-day session, so a series of these on one
    /// user id is the one signal that someone is inside. Never an address, a code or a token.
    /// </summary>
    void ReauthenticationFailed(Guid userId, Auth.Grants.GrantPurpose purpose);
}
