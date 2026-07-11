namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Centralized <see cref="Jobbliggaren.Domain.Common.DomainError"/> codes for the
/// auth flow. Keeps control-flow discriminants and wire mapping in ONE place
/// (§5 — no magic strings scattered across Application/Infrastructure/Api).
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// Generic, deliberately vague credential failure: unknown email, wrong password
    /// or a soft-deleted account. Rendered as 401 (AuthEndpoints) with copy that never
    /// reveals which of the causes applied (account-enumeration avoidance).
    /// </summary>
    public const string InvalidCredentials = "Auth.InvalidCredentials";

    /// <summary>
    /// The single user-facing detail for the <see cref="InvalidCredentials"/> 401. Rendered on the
    /// wire ONLY via <c>AuthProblem.InvalidCredentials()</c> (Api); referenced from here so the
    /// Result-idiom <c>DomainError</c> message in <c>ReauthenticationService</c> (which never reaches
    /// the wire — normalized by AuthProblem in both the behavior and /auth/verify paths) cannot
    /// silently drift from the authoritative copy (dotnet-architect PR2c-1 Minor — single source).
    /// </summary>
    public const string InvalidCredentialsMessage = "E-post eller lösenord är felaktigt.";

    /// <summary>
    /// Internal lockout verdict (#503, OWASP A07): the account is temporarily locked
    /// after too many failed attempts. Discriminates the audit event
    /// (<c>account_locked_out</c>) in the Api handler BUT is normalized to a
    /// byte-identical <see cref="InvalidCredentials"/> response on the wire
    /// (<c>AuthEndpoints.ToErrorResult</c>) so lockout state never leaks as an
    /// account-enumeration or DoS-target oracle. This code must NEVER reach the client.
    /// </summary>
    public const string AccountLocked = "Auth.AccountLocked";

    /// <summary>
    /// Generic, non-enumerating registration failure (#481 Low): a duplicate email/username is
    /// collapsed to this so a legacy (flag-OFF) 400 response reveals neither which field failed nor
    /// the submitted address (vs Identity's raw English "Username 'x' is already taken").
    /// <para>
    /// #714: with email-confirmation-first registration ON, this code is an INTERNAL DISCRIMINANT
    /// ONLY (like <see cref="AccountLocked"/>) — <c>RegisterCommandHandler</c> swallows the duplicate,
    /// returns the SAME 202 as a fresh signup and emails an out-of-band account-exists notice, so a
    /// taken address is indistinguishable from a free one on both status and body (the 200-vs-400
    /// status oracle is closed). This code and <see cref="DuplicateAccountMessage"/> MUST NEVER reach
    /// the wire on the flag-ON path. Rendered as 400 only on the legacy flag-OFF path.
    /// </para>
    /// </summary>
    public const string DuplicateAccount = "Auth.DuplicateAccount";

    /// <summary>
    /// The single user-facing detail for <see cref="DuplicateAccount"/>. No address echo, no field
    /// name; hints the recovery path (log in) without confirming more than the 400 status already does.
    /// Legacy flag-OFF path only (see <see cref="DuplicateAccount"/>).
    /// </summary>
    public const string DuplicateAccountMessage =
        "Det gick inte att skapa kontot. Om du redan har ett konto kan du logga in i stället.";

    /// <summary>
    /// #714 — login gate for email-confirmation-first registration. Emitted by
    /// <c>UserAccountService.ValidateCredentialsAsync</c> only when the flag is ON, the password is
    /// CORRECT, and <c>ApplicationUser.EmailConfirmed</c> is false. Because it is reachable only after
    /// a valid password it is NOT an account-enumeration oracle (a wrong password / unknown account
    /// still yields the byte-identical <see cref="InvalidCredentials"/> 401). The Api renders it as a
    /// distinct <c>403</c> with an actionable message (endpoint-local arm, no new ErrorKind). The
    /// re-auth path (<c>ReauthenticationService</c>) normalizes it back to
    /// <see cref="InvalidCredentials"/> so the re-auth surface stays a uniform 401 (it is unreachable
    /// there — only confirmed users hold sessions — but defense-in-depth).
    /// </summary>
    public const string EmailNotConfirmed = "Auth.EmailNotConfirmed";

    /// <summary>
    /// The single user-facing detail for the <see cref="EmailNotConfirmed"/> 403. Actionable (§10):
    /// tells a legitimate unconfirmed user how to proceed instead of a misleading wrong-password 401.
    /// </summary>
    public const string EmailNotConfirmedMessage =
        "Bekräfta din e-postadress för att logga in. Vi har skickat en länk till din inkorg.";

    /// <summary>
    /// #714 — uniform failure for EVERY rejection on the PUBLIC registration-confirm endpoint
    /// (<c>POST /auth/verify-email</c>): unknown user, malformed/bad/expired token. A public confirm
    /// endpoint must not distinguish them or it becomes an account-existence oracle (parity with
    /// <c>Auth.InvalidEmailChangeToken</c>, #679). Rendered as 400 via the central kind-mapper.
    /// </summary>
    public const string InvalidEmailConfirmationToken = "Auth.InvalidEmailConfirmationToken";

    /// <summary>
    /// The single user-facing detail for <see cref="InvalidEmailConfirmationToken"/>. No account/field
    /// disclosure; points to the recovery path (register again for a fresh link).
    /// </summary>
    public const string InvalidEmailConfirmationTokenMessage =
        "Bekräftelselänken är ogiltig eller har gått ut. Registrera dig igen för att få en ny länk.";

    /// <summary>
    /// #703 — the authenticated change-email request is inside its per-user or per-target anti-email-bomb
    /// cooldown window. Rendered as a VISIBLE 409 via the central kind-mapper (unlike the unauthenticated
    /// resend / account-exists silent no-op): the change-email surface already leaks existence via the
    /// <c>Auth.EmailTaken</c> 409, so the anti-enum silence buys nothing here and a "wait a moment" is
    /// better UX than a false "link sent". The per-user throttle is checked first (short-circuit) so a
    /// blocked actor cannot also extend a victim's window.
    /// </summary>
    public const string ChangeEmailCooldown = "Auth.ChangeEmailCooldown";

    /// <summary>
    /// The single user-facing detail for <see cref="ChangeEmailCooldown"/> (§10, civic tone; no address
    /// echo, actionable — tells the user to wait).
    /// </summary>
    public const string ChangeEmailCooldownMessage =
        "Du begärde nyligen ett adressbyte. Vänta en liten stund innan du försöker igen.";
}
