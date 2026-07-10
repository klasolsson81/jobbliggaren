namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Email-utskick för transactional flows (background-match notifications, ADR 0080 Vag 4).
/// Impl: ConsoleEmailSender (Infrastructure) — loggar via ILogger (MEL → Seq-sink,
/// TD-104) för lokal dev/MVP; Dev/Test-only (security-auditor Major #1, STEG 6),
/// NullEmailSender i andra miljöer. Transaktionell mejlväg via Resend (TD-101, ADR 0066 —
/// AWS SES borttaget). Templates på svenska per civic-utility-design.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Skickar en bakgrundsmatchnings-notis (ADR 0080 Vag 4 PR-4). <paramref name="content"/>
    /// är icke-PII (jobbtitlar + företag + grad-labels, aldrig en siffra/CV-data); mottagar-
    /// adressen bärs separat i <paramref name="toEmail"/>. Mallen lägger en OBLIGATORISK
    /// inställnings-/avregistreringslänk (GDPR Art. 7(3)). Consent-grindas av anroparen
    /// (opt-in OFF default, withdrawal stoppar omedelbart — ADR 0080 Beslut 5).
    /// <para>
    /// <paramref name="idempotencyKey"/> är en deterministisk, PII-fri idempotensmarkör
    /// (ADR 0080 PR-4 item 4, #187) som det transaktionella utskicket (Resend) använder för att
    /// inte dubbel-leverera vid en transport-retry. Icke-transaktionella impls (Console/Null)
    /// ignorerar den.
    /// </para>
    /// </summary>
    Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey,
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
    /// <paramref name="idempotencyKey"/> är en deterministisk, PII-fri idempotensmarkör
    /// (namespace <c>follow/v1/…</c>) som Resend använder för att inte dubbel-leverera vid en
    /// transport-retry. Icke-transaktionella impls (Console/Null) ignorerar den.
    /// </para>
    /// </summary>
    Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        FollowedCompanyNotificationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the change-email OWNERSHIP CONFIRMATION (#679) to the NEW address. <paramref name="content"/>
    /// carries the recipient's own new address + an opaque, single-use, URL-safe token the template
    /// builds the confirmation link from (<c>{BaseUrl}/bekrafta-epost?uid=&amp;email=&amp;token=</c>). The
    /// address is NOT changed until the link is opened. This is the codebase's first
    /// token-&gt;email-&gt;confirm path (registration is not email-confirmed).
    /// <para>
    /// <paramref name="idempotencyKey"/> is a deterministic, PII-free marker the transactional sender
    /// (Resend) uses to avoid double-delivery on a transport retry. Non-transactional impls
    /// (Console/Null) ignore it.
    /// </para>
    /// </summary>
    Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        EmailChangeConfirmationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the "your email address was changed" SECURITY NOTICE (#679, CTO-bind #4) to the OLD
    /// address after a completed change, so the previous owner can detect an unauthorized change
    /// (OWASP ASVS V2.5 / NIST SP 800-63B). Carries NO token, NO link to the new address, and does NOT
    /// reveal the new address - only a factual notice + a help-centre link built template-side from
    /// <c>EmailOptions.BaseUrl</c>. <paramref name="idempotencyKey"/> dedupes a transport retry;
    /// Console/Null ignore it.
    /// </summary>
    Task SendEmailChangedNotificationAsync(
        string toEmail,
        EmailChangedNotificationIdempotencyKey idempotencyKey,
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
    /// <paramref name="idempotencyKey"/> is a deterministic, PII-free marker the transactional sender
    /// (Resend) uses to avoid double-delivery on a transport retry. Non-transactional impls
    /// (Console/Null) ignore it.
    /// </para>
    /// </summary>
    Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        EmailConfirmationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends the registration ACCOUNT-EXISTS notice (#714) out-of-band to a TAKEN address when someone
    /// attempts to register it. Carries NO token, NO link that grants access - only a factual notice +
    /// a login link built template-side from <c>EmailOptions.BaseUrl</c>, so a real account owner is
    /// told someone tried to register their address (login-nudge, Klas decision) while the HTTP response
    /// stays an identical 202 (no enumeration signal). Mirrors the change-email old-address notice.
    /// <para>
    /// <paramref name="idempotencyKey"/> dedupes a transport retry AND repeated attempts on the same
    /// address (anti-email-bomb); Console/Null ignore it.
    /// </para>
    /// </summary>
    Task SendAccountExistsNoticeAsync(
        string toEmail,
        AccountExistsNoticeIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken);
}
