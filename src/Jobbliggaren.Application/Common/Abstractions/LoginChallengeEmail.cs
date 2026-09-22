using Jobbliggaren.Application.Auth.LoginChallenges;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// The login-challenge mail's content (#1735, ADR 0142 D2): exactly one variant per mail, chosen by the plan
/// at issue time. A closed set — the constructor is private, so only the variants nested here exist. None
/// carries the recipient, which travels separately.
/// </summary>
public abstract record LoginChallengeEmail
{
    private LoginChallengeEmail()
    {
    }

    /// <summary>An existing account within its code budget: a code and a link.</summary>
    public sealed record CodeAndLink(LoginCode Code, LoginLinkToken Link) : LoginChallengeEmail;

    /// <summary>
    /// An existing account whose code budget is spent: the link only, so a third party who spends the
    /// budget cannot lock the owner out (Klas, 2026-09-19, option (A)).
    /// </summary>
    public sealed record LinkOnly(LoginLinkToken Link) : LoginChallengeEmail;

    /// <summary>
    /// An address with no account, while registration is not open: no credential. Recipient class (3) —
    /// the address may have been typed by someone else, so the mail carries an Art. 14 notice.
    /// </summary>
    public sealed record RegistrationClosed : LoginChallengeEmail;

    /// <summary>
    /// An account in its restore window: no credential, the date the account goes and the way back (via
    /// the contact address; login never restores it).
    /// </summary>
    public sealed record PendingDeletion(DateOnly PermanentDeletionEarliest) : LoginChallengeEmail;

    /// <summary>
    /// An address with no account while registration is open, within the code budget: the code that leads
    /// to an account. No link — a magic link is for an existing account only (ADR 0142 D1).
    /// </summary>
    public sealed record NewAccountCode(LoginCode Code) : LoginChallengeEmail;

    /// <summary>An address with no account while registration is open, past the code budget: no credential.</summary>
    public sealed record NewAccountCodeLimitReached : LoginChallengeEmail;

    /// <summary>
    /// A re-authentication code for a signed-in user (#1739, ADR 0142 D5), sent to the account's own address.
    /// A code and never a link: a link yields a session, never a re-authentication. No Art. 14 notice — the
    /// recipient is the account holder, whose address the account already holds.
    /// </summary>
    public sealed record ReauthenticationCode(LoginCode Code) : LoginChallengeEmail;

    /// <summary>
    /// The code that proves a NEW address before a change-email completes (#1739, ADR 0142 D5), sent to that
    /// address. A code and never a link. Recipient class (3): the address sits on no account, and whoever
    /// typed it may not own it, so the mail carries the whole Art. 14 notice. <see cref="TargetWindow"/> is the
    /// target cooldown's window, the one fingerprint of the address the request keeps; it is configuration, so
    /// the handler carries it in from the options it ran on.
    /// </summary>
    public sealed record AddressChangeCode(LoginCode Code, TimeSpan TargetWindow) : LoginChallengeEmail;
}
