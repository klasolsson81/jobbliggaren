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
    /// reveal the new address - only a factual notice + a help-centre link built template-side from
    /// <c>EmailOptions.BaseUrl</c>. Sent at most once per completed change by construction: the
    /// change itself is the single trigger.
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
}
