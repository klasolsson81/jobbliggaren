namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Email-utskick för transactional flows (background-match notifications, ADR 0080 Vag 4).
/// Impl: ConsoleEmailSender (Infrastructure) — loggar via ILogger (MEL → Seq-sink,
/// TD-104) för lokal dev/MVP; Dev/Test-only (security-auditor Major #1, STEG 6),
/// NullEmailSender i andra miljöer. Transaktionell mejlväg via Amazon SES v2 i eu-north-1
/// (ADR 0124, #1237 — HTTPS-API, aldrig SMTP). Templates på svenska per civic-utility-design.
/// <para>
/// <b>Ingen idempotensparameter, och det är ett beslut (ADR 0124, senior-cto-advisor
/// 2026-08-08).</b> Porten bar tidigare en typad idempotensnyckel per metod. Den var en
/// Resend-artefakt hela vägen ned i sin egen invariant (<c>"at most 256 chars (Resend limit)"</c>)
/// och SES v2 <c>SendEmail</c> har ingen motsvarighet — inget <c>ClientToken</c>, ingen
/// dedup-parameter (mätt mot API-referensen 2026-08-08). Att behålla den hade lämnat en
/// Application-ägd port som bär en avvecklad leverantörs trådformat, som ingen implementation
/// kan konsumera (ISP). <b>Vad som faktiskt skyddade vad, efter mätning:</b> dedup ÖVER anrop
/// ägs en nivå upp — av claim-then-send-spinen plus <c>StrandedMatchReaperJob</c> för
/// notiserna, och av <c>ICooldownGate</c> för kontolivscykeln. ADR 0103 säger det uttryckligen
/// om anti-email-bomb-kontrollen: den är <i>"provider-independent (works regardless of Resend's
/// own idempotency-key dedup)"</i>. Kvar fanns bara transport-retry INOM en dispatch, och den
/// stängs av <c>MaxErrorRetry = 0</c> på SES-klienten.
/// </para>
/// <para>
/// <b>Undantagskontrakt (ADR 0124, senior-cto-advisor bind 4).</b> En implementation som
/// misslyckas kastar <see cref="Exceptions.EmailDeliveryException"/>, som bär e-postens KIND och
/// det underliggande undantagets TYPNAMN — ingenting annat, och med <c>InnerException</c>
/// avsiktligt TOM. Leverantörens eget undantag får ALDRIG lämna adaptern: Amazon SES lägger
/// mottagaradressen i sina felmeddelanden, många <c>[LoggerMessage]</c>-deklarationer
/// vidarebefordrar ett <see cref="Exception"/>-objekt till sänkan (antalet och dess grep bor i
/// ADR 0124), och <c>Api/Program.cs</c> har ingen generisk <c>catch</c> som stoppar ett
/// omatchat. Ett undantag ÄR
/// en osynlig del av en signatur, så kontraktet står här och inte bara i implementationen — och
/// <c>ConfirmEmailChangeCommandHandler</c>:s lokala <i>"§5 parity with the sender boundary"</i>
/// blir därmed den allmänna regeln i stället för en handlares egen disciplin.
/// </para>
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Whether this sender actually delivers. <b>The contract is: <c>SendXAsync</c> delivers if and
    /// only if this is <see langword="true"/>; a caller whose own success DEPENDS on delivery must
    /// consult it and refuse up front rather than report success afterwards.</b>
    /// <para>
    /// <b>This exists because <c>NullEmailSender</c> was an LSP violation without it (#1087).</b> It
    /// is registered as a valid <see cref="IEmailSender"/> in every non-Development/Test environment
    /// and is the live default today (<c>Email:Provider</c> is unset in every committed
    /// <c>appsettings*.json</c>). Dropping a notification is correct — a missed convenience. Dropping
    /// an ownership-confirmation link is not: <c>ChangeEmailCommandHandler</c> minted a token, mailed
    /// it into the void, returned <c>Result.Success</c> and had a <c>User.EmailChangeRequested</c>
    /// audit row stamped, while the address is only ever swapped when the link is opened. The user
    /// was told an action completed that could not occur, with no way forward. Without a member on
    /// the port the two callers cannot be told apart, so <b>every new delivery-dependent path
    /// inherited the defect silently</b> — which is the reason this is a widening of the CONTRACT and
    /// not a fix in one handler.
    /// </para>
    /// <para>
    /// <b>A constant per implementation, never a per-message question</b> (senior-cto-advisor Q3(b)
    /// 2026-07-26, dotnet-architect optionsset 2026-08-09). Delivery-dependence is a property of the
    /// CALLER, not of the sender: no implementation would answer differently per email kind — Null
    /// drops every one, SES sends every one — so a <c>CanDeliver(kind)</c> overload would carry a
    /// parameter that is dead by construction, and would invent a second enumeration of this port's
    /// send methods to keep in sync with them. The BCL precedent for one type plus capability queries
    /// over a lattice of interfaces is <see cref="System.IO.Stream.CanRead"/>/<c>CanSeek</c>/
    /// <c>CanWrite</c>.
    /// </para>
    /// <para>
    /// <b>The value never comes from the environment.</b> Application does not know what a
    /// "Production" is and cannot ask — <c>Jobbliggaren.Application.csproj</c> has no
    /// <c>Microsoft.Extensions.Hosting</c> reference, so an <c>IHostEnvironment</c> branch in a
    /// handler would not compile (CLAUDE.md §2.1). The environment-to-capability translation already
    /// lives in Infrastructure, in <c>AddEmailSender</c>'s choice of WHICH class to register; each
    /// class then answers for itself.
    /// </para>
    /// </summary>
    bool CanDeliver { get; }

    /// <summary>
    /// Skickar en bakgrundsmatchnings-notis (ADR 0080 Vag 4 PR-4). <paramref name="content"/>
    /// är icke-PII (jobbtitlar + företag + grad-labels, aldrig en siffra/CV-data); mottagar-
    /// adressen bärs separat i <paramref name="toEmail"/>. Mallen lägger en OBLIGATORISK
    /// inställnings-/avregistreringslänk (GDPR Art. 7(3)). Consent-grindas av anroparen
    /// (opt-in OFF default, withdrawal stoppar omedelbart — ADR 0080 Beslut 5).
    /// <para>
    /// Dubbel-leverans förhindras av claim-then-send-spinen (<c>NotificationStatus</c>
    /// Pending→Queued→Sent) plus <c>StrandedMatchReaperJob</c>, som markerar en strandad
    /// rad Failed och ALDRIG skickar om — inte av en provider-nyckel (ADR 0124).
    /// </para>
    /// </summary>
    Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Skickar en företagsföljnings-notis (ADR 0087 D5, #311 PR-4) — en sammanfattning av nya
    /// annonser från arbetsgivare användaren följer. En SEPARAT väg från
    /// <see cref="SendMatchNotificationEmailAsync"/> (senior-cto-advisor D1): en följnings-träff har
    /// INGEN grad, så <paramref name="content"/> bär bara publika annons-fält (titel + företag),
    /// aldrig en grad-label/siffra/CV-data eller org.nr (ADR 0087 D8 — personnummer-formad org.nr
    /// surfas aldrig; följnings-mejlet visar det publika företagsNAMNET). Mottagar-adressen bärs
    /// separat i <paramref name="toEmail"/>; mallen lägger en OBLIGATORISK inställnings-/
    /// avregistreringslänk (GDPR Art. 7(3)). Consent-grindas av anroparen (den SEPARATA
    /// FollowedCompanyNotificationsEnabled-flaggan, opt-in OFF default, withdrawal stoppar omedelbart).
    /// <para>
    /// Dubbel-leverans förhindras av samma claim-then-send-spine som matchnings-vägen (ADR 0124).
    /// </para>
    /// </summary>
    Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the change-email OWNERSHIP CONFIRMATION (#679) to the NEW address. <paramref name="content"/>
    /// carries the recipient's own new address + an opaque, single-use, URL-safe token the template
    /// builds the confirmation link from (<c>{BaseUrl}/bekrafta-epost?uid=&amp;email=&amp;token=</c>). The
    /// address is NOT changed until the link is opened. This is the codebase's first
    /// token-&gt;email-&gt;confirm path (registration is not email-confirmed).
    /// <para>
    /// Repeated sends are bounded by <c>ICooldownGate</c> on BOTH
    /// <c>CooldownScopes.ChangeEmailUser</c> (per actor) and <c>CooldownScopes.ChangeEmailTarget</c>
    /// (per new address) before the send (ADR 0103, <c>ChangeEmailCommandHandler</c>) — the VISIBLE
    /// half of the asymmetry (409), since the surface is authenticated. Provider-independent by
    /// construction.
    /// </para>
    /// </summary>
    Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the "your email address was changed" SECURITY NOTICE (#679, CTO-bind #4) to the OLD
    /// address after a completed change, so the previous owner can detect an unauthorized change
    /// (OWASP ASVS V2.5 / NIST SP 800-63B). Carries NO token, NO link to the new address, and does NOT
    /// reveal the new address - only a factual notice plus the contact address. It carries NO site
    /// link of any kind, which is a security property rather than an omission: the account's address
    /// has just been repointed, so a reset link would deliver the reset to the ATTACKER's inbox, and
    /// nothing on the site can help the rightful owner in that state. Sent at most once per completed
    /// change by construction: the change itself is the single trigger.
    /// </summary>
    Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the registration EMAIL-CONFIRMATION link (#714) to the account's own address after signup.
    /// <paramref name="content"/> carries the recipient's userId + an opaque, Base64Url token the
    /// template builds the activation link from (<c>{BaseUrl}/bekrafta-konto?uid=&amp;token=</c>). Until
    /// the link is opened the account cannot log in (the <c>EmailConfirmed</c> gate). This closes the
    /// registration status-oracle: the response is an identical 202 for a fresh or a taken address, and
    /// the confirmation link is the only out-of-band signal (delivered only to an inbox the requester
    /// controls, i.e. a fresh address).
    /// <para>
    /// Two call sites, and only one of them can repeat: the fresh registration send
    /// (<c>RegisterCommandHandler</c>) happens once per accepted signup and is ungated, while the
    /// user-driven resend endpoint is bounded by <c>ICooldownGate</c> on
    /// <c>CooldownScopes.ResendConfirm</c> (ADR 0103, <c>ResendEmailConfirmationCommandHandler</c>) —
    /// SILENT, because the surface is unauthenticated and a visible cooldown would itself be an
    /// enumeration oracle.
    /// </para>
    /// </summary>
    Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the registration ACCOUNT-EXISTS notice (#714) out-of-band to a TAKEN address when someone
    /// attempts to register it. Carries NO token, NO link that grants access - only a factual notice +
    /// a login link built template-side from <c>EmailOptions.BaseUrl</c>, so a real account owner is
    /// told someone tried to register their address (login-nudge, Klas decision) while the HTTP response
    /// stays an identical 202 (no enumeration signal). Mirrors the change-email old-address notice.
    /// <para>
    /// <b>Anti-email-bomb lives in <c>ICooldownGate</c>, not here (ADR 0103).</b> The per-target,
    /// existence-independent, SILENT cooldown on <c>CooldownScopes.AccountExists</c>, checked in
    /// <c>RegisterCommandHandler</c> before this call, is what stops an attacker flooding a taken
    /// address; ADR 0103's Consequences state it works <i>"regardless of Resend's own
    /// idempotency-key dedup"</i>, which is why the control survived that provider's removal
    /// untouched. The port carried a second, weaker copy of this claim until ADR 0124.
    /// </para>
    /// </summary>
    Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the PASSWORD-RESET link (#1171) to the address that requested it. <paramref name="content"/>
    /// carries the userId plus an opaque Base64Url token the template builds
    /// <c>{BaseUrl}/aterstall-losenord?uid=&amp;token=</c> from.
    /// <para>
    /// <b>Delivery-dependent, and the strictest case on this port.</b> The password is only changed when
    /// the emailed link is opened, so a dropped send leaves the requester with a "check your inbox"
    /// message, no link, and no way back into the account — which is the whole defect #1171 exists to
    /// close. <c>RequestPasswordResetCommandHandler</c> therefore consults <see cref="CanDeliver"/> and
    /// refuses with a 503 BEFORE any token is minted.
    /// </para>
    /// <para>
    /// <b>That refusal is the FIRST statement of the handler, and the ordering is load-bearing rather
    /// than tidy.</b> The surface is unauthenticated and answers a uniform 202 for known and unknown
    /// addresses alike. A capability check placed AFTER the account lookup would only ever be reachable
    /// when an account exists, making the 503 itself an existence oracle — the trap
    /// <c>ResendEmailConfirmationCommandHandler</c> avoids by never returning 503 at all. Checked first,
    /// the 503/202 split is a property of the server's configuration, evaluated before the submitted
    /// address is read, so it can carry no information about any account.
    /// </para>
    /// <para>
    /// Single-use without any stored token: <c>ResetPasswordAsync</c> rotates the user's SecurityStamp,
    /// which the token is bound to. Lifespan is <c>PasswordResetTokenProviderOptions.LifespanMinutes</c>,
    /// shorter than the other link kinds and enforced by its own token provider. Anti-email-bomb is
    /// <c>ICooldownGate</c> on <c>CooldownScopes.PasswordReset</c> — SILENT, for the same reason the
    /// resend cooldown is: a visible throttle on an unauthenticated surface is itself an oracle.
    /// </para>
    /// </summary>
    Task SendPasswordResetAsync(
        string toEmail,
        PasswordResetEmail content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the PASSWORD-CHANGED security notice (#1171) after a completed reset, to the address the
    /// reset was performed for. Carries NO token and NO link that grants access — it is a factual
    /// notice, the breach-detection control OWASP ASVS V2.5 and NIST SP 800-63B ask for on a credential
    /// change, and the twin of <see cref="SendEmailChangedNotificationAsync"/>.
    /// <para>
    /// A password reset is an account-takeover vector by construction: whoever holds the inbox holds the
    /// account. This notice is what lets a real owner notice a reset they did not perform, at the one
    /// moment they still could act on it.
    /// </para>
    /// <para>
    /// <b><c>NullEmailSender</c> dropping this is unreachable rather than tolerated.</b> No reset token
    /// can be minted while <see cref="CanDeliver"/> is false, so the event this notice reports cannot
    /// occur with a sender that would drop it. It needs no gate of its own.
    /// </para>
    /// </summary>
    Task SendPasswordChangedNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken);
}
