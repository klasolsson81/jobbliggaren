namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Centralized <see cref="Jobbliggaren.Domain.Common.DomainError"/> codes for the
/// auth flow. Keeps control-flow discriminants and wire mapping in ONE place
/// (§5 — no magic strings scattered across Application/Infrastructure/Api).
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// Generic, deliberately vague refusal to re-authenticate: a missing, spent or foreign grant, a
    /// soft-deleted account, or an account without an address. Rendered as 401 (AuthEndpoints) with copy
    /// that never reveals which of the causes applied.
    /// </summary>
    public const string InvalidCredentials = "Auth.InvalidCredentials";

    /// <summary>
    /// A handler's self-defending refusal when <c>ICurrentUser</c> carries no user: AuthorizationBehavior ran
    /// before it, so this is reached only when the pipeline is misconfigured. Validation → 400.
    /// </summary>
    public const string NotAuthenticated = "Auth.NotAuthenticated";

    /// <summary>
    /// A handler's self-defending refusal of input its validator already refuses: ValidationBehavior ran before
    /// it, so this is reached only when the pipeline is misconfigured. Validation → 400.
    /// </summary>
    public const string InvalidInput = "Auth.InvalidInput";

    /// <summary>The account a user id names is gone. NotFound → 404.</summary>
    public const string UserNotFound = "Auth.UserNotFound";

    /// <summary>
    /// The single user-facing detail for the <see cref="InvalidCredentials"/> 401. Rendered on the
    /// wire ONLY via <c>AuthProblem.InvalidCredentials()</c> (Api); referenced from here so the
    /// Result-idiom <c>DomainError</c> message in <c>ReauthenticationService</c> (which never reaches
    /// the wire — normalized by AuthProblem in the behavior path) cannot
    /// silently drift from the authoritative copy (dotnet-architect PR2c-1 Minor — single source).
    /// </summary>
    public const string InvalidCredentialsMessage = "Det gick inte att bekräfta att det är du.";

    /// <summary>
    /// An address or user name that already has an account, collapsed to one code so neither the field nor
    /// the submitted address is echoed (vs Identity's raw English "Username 'x' is already taken"). An
    /// INTERNAL DISCRIMINANT: <c>AccountRegistrar</c> treats it as success, because the address has an account
    /// afterwards, so this code and <see cref="DuplicateAccountMessage"/> never reach the wire.
    /// </summary>
    public const string DuplicateAccount = "Auth.DuplicateAccount";

    /// <summary>The message <see cref="DuplicateAccount"/> carries. No address echo, no field name.</summary>
    public const string DuplicateAccountMessage =
        "Det gick inte att skapa kontot. Om du redan har ett konto kan du logga in i stället.";

    /// <summary>
    /// #703 — the authenticated change-email request is refused by one of its anti-email-bomb budgets: the
    /// per-user cooldown, the per-address cooldown, or the per-address daily cap (#1739). The three share this
    /// code because the last two are shared between users, and a refusal that told them apart would say that
    /// someone else asked for the address. Rendered as a VISIBLE 409 via the central kind-mapper: the
    /// change-email surface already leaks existence via the <c>Auth.EmailTaken</c> 409, so an anti-enum
    /// silence would buy nothing here. The per-user throttle is
    /// checked first (short-circuit) so a blocked actor cannot also extend a victim's window.
    /// </summary>
    public const string ChangeEmailCooldown = "Auth.ChangeEmailCooldown";

    /// <summary>
    /// The single user-facing detail for <see cref="ChangeEmailCooldown"/> (§10, civic tone; no address echo).
    /// It holds for every producer: the caller may have asked nothing (the two budgets are shared between
    /// users) and the wait is a minute or a day, so it names neither.
    /// </summary>
    public const string ChangeEmailCooldownMessage =
        "Det går inte att begära ett adressbyte just nu. Försök igen senare.";

    /// <summary>
    /// #1739 — a re-authentication code was requested inside the account's own cooldown. The caller is signed in,
    /// so the refusal is visible. Conflict → 409.
    /// </summary>
    public const string ReauthCooldown = "Auth.ReauthCooldown";

    public const string ReauthCooldownMessage =
        "Du begärde nyligen en kod. Vänta en liten stund innan du försöker igen.";

    /// <summary>
    /// #1739 — the account's re-authentication codes for the day are spent
    /// (<c>LoginChallengePolicy.ReauthCodeBudget</c>). Terminal: unlike the login challenge there is no link to
    /// fall back to, since a link yields a session and never a re-authentication, so the message says a day
    /// and not a moment. Only a holder of the session can spend this budget. Conflict → 409.
    /// </summary>
    public const string ReauthCodeBudgetExhausted = "Auth.ReauthCodeBudgetExhausted";

    public const string ReauthCodeBudgetExhaustedMessage =
        "Du har begärt så många koder som går på ett dygn. Försök igen i morgon.";

    /// <summary>
    /// The address a change-email names is already some account's address or user name (#679; both since
    /// #1739). Answered at the request step and, for a race the request step lost, at the swap. The route is
    /// authenticated and re-authenticated, and the per-user budgets run first, so the refusal is visible.
    /// Conflict → 409.
    /// </summary>
    public const string EmailTaken = "Auth.EmailTaken";

    public const string EmailTakenMessage = "Den e-postadressen är upptagen.";

    /// <summary>
    /// #1739 — the user has asked to move to as many new addresses as a day admits
    /// (<c>ChangeEmailPolicy.UserTargetsDailyBudget</c>). Keyed by the user id, so only a holder of the session can
    /// spend it. Conflict → 409.
    /// </summary>
    public const string ChangeEmailTargetBudgetExhausted = "Auth.ChangeEmailTargetBudgetExhausted";

    public const string ChangeEmailTargetBudgetExhaustedMessage =
        "Du har bett om att byta till så många nya adresser som går på ett dygn. Försök igen i morgon.";

    /// <summary>
    /// #1739 — the change-email grant cannot be redeemed: unknown, expired, already used, or issued for another
    /// user or address. One answer, the <see cref="LoginGrantUnusable"/> form. Gone → 410.
    /// </summary>
    public const string EmailChangeGrantUnusable = "Auth.EmailChangeGrantUnusable";

    public const string EmailChangeGrantUnusableMessage =
        "Det gick inte att slutföra bytet. Börja om med att begära en ny kod.";

    /// <summary>
    /// #1739 — the swap did not complete: the address write, or a user-name write
    /// that failed for any reason but a taken name. Conflict → 409.
    /// </summary>
    public const string EmailChangeIncomplete = "Auth.EmailChangeIncomplete";

    public const string EmailChangeIncompleteMessage = "Bytet gick inte att slutföra. Försök igen om en stund.";

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
    /// <b>The user never sees this string.</b> The frontend renders its own localised copy and never the
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
    /// change-email was the only producer; the forgot-password request (retired in ADR 0142 part 5a) was the
    /// second, and there no
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

    // ── #1735 — the login challenge's answers to a presented code or link (ADR 0142 D3). The cookie holder
    // is told which of wrong / expired / burned happened: they minted the challenge, a record is always
    // written, so the distinction carries no account information. Missing and expired are ONE code. As with
    // the codes above, the browser renders its own copy; these messages follow the page form's wording.

    /// <summary>A wrong code, with more than one attempt left. Validation → 400.</summary>
    public const string LoginCodeWrong = "Auth.LoginCodeWrong";

    public const string LoginCodeWrongMessage = "Koden stämmer inte. Kontrollera siffrorna och försök igen.";

    /// <summary>A wrong code, with exactly one attempt left: the page warns before the burn. Validation → 400.</summary>
    public const string LoginCodeWrongLastAttempt = "Auth.LoginCodeWrongLastAttempt";

    public const string LoginCodeWrongLastAttemptMessage =
        "Koden stämmer inte. Ett försök kvar. Sedan behöver du begära en ny kod.";

    /// <summary>The code arm is burned after the last wrong attempt. Gone → 410.</summary>
    public const string LoginCodeBurned = "Auth.LoginCodeBurned";

    public const string LoginCodeBurnedMessage =
        "Du har skrivit fel kod tre gånger. Av säkerhetsskäl behöver du en ny kod.";

    /// <summary>No live challenge: expired, used, replaced or never written — one answer. Gone → 410.</summary>
    public const string LoginCodeExpired = "Auth.LoginCodeExpired";

    public static readonly string LoginCodeExpiredMessage =
        $"Koden har gått ut. Den gäller i {(int)LoginChallenges.LoginChallengePolicy.ChallengeTtl.TotalMinutes} minuter.";

    /// <summary>A link that cannot be used, for any reason — one answer. Gone → 410.</summary>
    public const string LoginLinkUnusable = "Auth.LoginLinkUnusable";

    public const string LoginLinkUnusableMessage =
        "Länken går inte att använda. Begär en ny kod på inloggningssidan.";

    /// <summary>
    /// The submitted address carries a character no account's stored address may hold: a control, format,
    /// surrogate or whitespace character. A property of the submitted spelling alone, so it says nothing about
    /// any account. Validation → 400.
    /// </summary>
    public const string EmailNotStorable = "Auth.EmailNotStorable";

    public const string EmailNotStorableMessage =
        "E-postadressen innehåller tecken som inte kan användas. Kontrollera adressen och försök igen.";

    /// <summary>
    /// A grant that cannot be redeemed, for any reason: unknown, expired, already used, or a registration
    /// claim another request holds. One answer. Gone → 410.
    /// </summary>
    public const string LoginGrantUnusable = "Auth.LoginGrantUnusable";

    public const string LoginGrantUnusableMessage =
        "Det gick inte att slutföra registreringen. Begär en ny kod på inloggningssidan.";
}
