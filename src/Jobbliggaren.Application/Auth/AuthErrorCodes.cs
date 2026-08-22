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
    /// <para>
    /// #1349 — it states what the gate establishes and nothing more. It used to say confirming was
    /// enough to log in; for a profile-less row it is not, and that row reaches THIS surface rather
    /// than the uniform 401, because ValidateCredentialsAsync returns EmailNotConfirmed before the
    /// JobSeeker guard runs. The second sentence became an instruction for the same reason: the gate
    /// establishes EmailConfirmed=false and knows nothing about whether a send succeeded.
    /// </para>
    /// <para>
    /// Keep it a <c>const</c>. Making it state-dependent is the executable form of the prohibition on
    /// letting an unauthenticated surface read account state (senior-cto-advisor 2026-08-22).
    /// </para>
    /// </summary>
    public const string EmailNotConfirmedMessage =
        "Din e-postadress är inte bekräftad än. Kontrollera din inkorg.";

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

    /// <summary>
    /// The public-registration kill-switch is CLOSED (<c>Auth:RegistrationsOpen</c> = false;
    /// ADR 0083 Amendment 2026-08-03). Rendered as an endpoint-local 503 by
    /// <c>AuthEndpoints.ToErrorResult</c>, not via the kind-union — see that arm for why.
    /// <para>
    /// Not an enumeration oracle, and not by care but by construction: the gate is the FIRST
    /// statement of the handler and never reads the submitted address, so the response cannot vary
    /// with it. That is a stronger property than #714's uniform 202, which needs two branches held
    /// byte-identical.
    /// </para>
    /// </summary>
    public const string RegistrationsClosed = "Auth.RegistrationsClosed";

    /// <summary>
    /// The single user-facing detail for <see cref="RegistrationsClosed"/> (§10: informative,
    /// non-blaming, no exclamation mark). Deliberately promises no opening date — the app will open,
    /// but a date we might miss is worse than none. Echoes the copy the retired kill-switch carried
    /// before ADR 0083 removed it.
    /// <para>
    /// <b>The user never sees this string.</b> The frontend renders its own localised copy
    /// (<c>auth.actions.registrationsClosed</c> in <c>messages/{sv,en}/pages.json</c>) and never the
    /// ProblemDetails <c>detail</c>; what it consumes is <see cref="RegistrationsClosed"/> as the
    /// discriminator. The two Swedish sentences are therefore independent by construction, not
    /// duplicated by accident — this one exists so a direct API consumer gets a civil answer too.
    /// </para>
    /// <para>
    /// The <c>Kind</c> stamped by <c>DomainError.Validation</c> is deliberately inert here: the
    /// endpoint arm matches on the code and renders 503 before the central kind-mapper is reached.
    /// Availability is not a kind the Domain-level union models, and adding one would put an ops
    /// concept in Domain.
    /// </para>
    /// </summary>
    public const string RegistrationsClosedMessage =
        "Registreringen är inte öppen ännu. Försök igen senare.";

    /// <summary>
    /// No transactional email provider is configured, so a flow whose success DEPENDS on delivery
    /// refuses up front instead of reporting a completed action that cannot occur (#1087). Raised by
    /// <c>ChangeEmailCommandHandler</c> when <c>IEmailSender.CanDeliver</c> is false — today the live
    /// default outside Development/Test, since <c>Email:Provider</c> is unset in every committed
    /// <c>appsettings*.json</c>.
    /// <para>
    /// Rendered as an endpoint-local <b>503</b> by <c>AuthEndpoints.ToErrorResult</c>, on the same
    /// ratified reasoning as <see cref="RegistrationsClosed"/>: server availability is a third axis,
    /// distinct from the 400/404/409/410 request/resource semantics the kind-union models, and a
    /// capacity deliberately withheld that returns when someone sets a config key is exactly
    /// RFC 9110 §15.6.4's case. <b>No new <c>ErrorKind</c></b> — that fork was named as requiring an
    /// architect optionsset before code (senior-cto-advisor 2026-07-26), and the optionsset
    /// (dotnet-architect 2026-08-09) closed it against extension by pointing at this precedent.
    /// </para>
    /// <para>
    /// <b>No <c>Retry-After</c></b>, for the precedent's own reason: the date on which the provider
    /// is configured is unknown, and a wrong <c>Retry-After</c> is worse than none because clients
    /// and caches honour it.
    /// </para>
    /// <para>
    /// The <c>Kind</c> stamped by <c>DomainError.Validation</c> is a FALLBACK CARRIER, not a semantic
    /// claim — same trade as <see cref="RegistrationsClosed"/>. Delete the endpoint arm and this
    /// degrades to 400, not 500, which is why the 503 is pinned by an integration test rather than
    /// left to the carrier.
    /// </para>
    /// <para>
    /// Not an enumeration oracle: the surface is authenticated AND re-authenticated, the check reads
    /// no request input, and the response cannot vary with the submitted address.
    /// </para>
    /// </summary>
    public const string EmailDeliveryUnavailable = "Auth.EmailDeliveryUnavailable";

    /// <summary>
    /// The single user-facing detail for <see cref="EmailDeliveryUnavailable"/> (§10: du-form,
    /// informative, non-blaming, no exclamation mark).
    /// <para>
    /// <b>This string is never what the client renders.</b> The browser copy is authored separately
    /// in <c>messages/{sv,en}/settings.json</c> under <c>account.errors.emailDeliveryUnavailable</c>;
    /// the action layer compares the ProblemDetails <c>title</c> against an exact whitelist and
    /// never renders backend <c>detail</c>. Keep the two in the same spirit, but a change here does
    /// not reach a user.
    /// </para>
    /// <para>
    /// <b>The client arm exists since #734 B-ii</b> (it did not until then: a 503 fell through to the
    /// generic <c>settings.account.errors.changeEmailFailed</c>, so the user learned neither the
    /// reason nor that the address was unchanged, and the submit button stayed live).
    /// <c>changeEmailAction</c> now returns a <c>refused</c> result on this title and the card
    /// replaces itself with a <c>role="status"</c> panel, removing the retry affordance.
    /// <b>It discriminates on the TITLE, never on the status alone</b> (the gate is conjunctive —
    /// status 503 AND the exact title), because this route has at least two other 503 producers:
    /// a Redis-backed <c>SessionStoreUnavailableException</c>, whose body carries no <c>title</c>
    /// key, and a reverse proxy, whose body is not JSON at all. A status-only arm would print
    /// "e-post är inte aktiverat" during an incident and mask it. Both counterfactuals are pinned in
    /// <c>me.change-email.test.ts</c>; do not relax the arm to a bare status check.
    /// </para>
    /// <para>
    /// <b>That did NOT close point 5.5</b> in <c>release-checklist.md</c> §2.6. The client arm was one
    /// of several conditions on the same trigger; condition (a) still expires only at a real
    /// <c>Email:Provider</c>, and (b) is untouched.
    /// </para>
    /// <para>
    /// <b>Generalised for #1171.</b> It read "…någon bekräftelselänk. Din adress är oförändrad." while
    /// change-email was the only producer; the forgot-password request is the second, and there no
    /// address was being changed, so that sentence would have been false. The code names an OPERATIONAL
    /// condition — no configured sender can deliver — which is flow-independent, so the detail is too.
    /// A second code for the same condition would have needed a second endpoint arm and a second
    /// frontend whitelist entry to say the same thing. Neither client renders this string, so no user
    /// copy changed.
    /// </para>
    /// </summary>
    public const string EmailDeliveryUnavailableMessage =
        "E-postutskick är inte aktiverat just nu, så vi kan inte skicka något e-postmeddelande. "
        + "Ingenting har ändrats. Försök igen senare.";

    /// <summary>
    /// A password-reset token was rejected (#1171): unknown user, malformed, wrong, or expired. ONE code
    /// for all four, deliberately — the reset endpoint is PUBLIC, and telling "no such account" apart
    /// from "bad token" would make it an account-existence oracle. Rendered 400 through the central
    /// kind-mapper; no endpoint-local arm.
    /// <para>
    /// <b>Password rejections do NOT collapse into this</b>, and that asymmetry is safe for a measured
    /// reason rather than a stylistic one: Identity verifies the token BEFORE running the password
    /// validators, so a password rejection is reachable only by someone already holding a valid token.
    /// It discloses nothing that person does not have, and a real user needs to know which rule they
    /// broke.
    /// </para>
    /// <para>
    /// On the wire that means <c>Auth.PwnedPassword</c> and nothing else. A too-short password never
    /// reaches <c>UserManager</c>: <c>ResetPasswordCommandValidator</c> carries the same 12-character
    /// floor as <c>IdentityOptions.Password.RequiredLength</c>, so <c>ValidationBehavior</c> fells it
    /// first and answers with the <c>{errors}</c> shape. <c>Auth.PasswordTooShort</c> IS producible by
    /// <c>IUserAccountService.ResetPasswordAsync</c> if called directly — the port maps every Identity
    /// error code — but no HTTP request can produce it here.
    /// </para>
    /// </summary>
    public const string InvalidPasswordResetToken = "Auth.InvalidPasswordResetToken";

    /// <summary>
    /// The single user-facing detail for <see cref="InvalidPasswordResetToken"/> (§10: du-form,
    /// informative, non-blaming, no exclamation mark). It names the recovery — request a new link —
    /// because the state is not the user's fault and is one click from being fixed. As with the other
    /// codes here, the browser renders its own localised copy and never this string.
    /// </summary>
    public const string InvalidPasswordResetTokenMessage =
        "Länken är ogiltig eller har gått ut. Begär en ny återställningslänk och försök igen.";
}
