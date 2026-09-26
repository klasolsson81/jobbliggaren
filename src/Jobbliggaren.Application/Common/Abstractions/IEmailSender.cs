namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Email-utskick för transactional flows (background-match notifications, ADR 0080 Vag 4).
/// Impl: ConsoleEmailSender (Infrastructure) — loggar via ILogger (MEL → Seq-sink,
/// TD-104) för lokal dev/MVP; Dev/Test-only (security-auditor Major #1, STEG 6),
/// NullEmailSender i andra miljöer. Transaktionell mejlväg via Scaleway Transactional Email i
/// fr-par (#183 — HTTPS-API, aldrig SMTP). Templates på svenska per civic-utility-design.
/// <para>
/// <b>Ingen idempotensparameter, och det är ett beslut (ADR 0124, senior-cto-advisor
/// 2026-08-08).</b> Porten bar tidigare en typad idempotensnyckel per metod. Den var en
/// Resend-artefakt hela vägen ned i sin egen invariant (<c>"at most 256 chars (Resend limit)"</c>)
/// och ingen av de leverantörer som följt har någon motsvarighet — varken SES v2 <c>SendEmail</c>
/// (mätt 2026-08-08) eller Scaleways <c>POST /emails</c> (mätt 2026-08-15) bär en
/// idempotens- eller dedup-parameter. Att behålla den hade lämnat en
/// Application-ägd port som bär en avvecklad leverantörs trådformat, som ingen implementation
/// kan konsumera (ISP). <b>Vad som faktiskt skyddade vad, efter mätning:</b> dedup ÖVER anrop
/// ägs en nivå upp — av claim-then-send-spinen plus <c>StrandedMatchReaperJob</c> för
/// notiserna, och av inloggningsutmaningens budgetar (<c>IRateBudget</c>) för kontomejlen.
/// Kvar fanns bara transport-retry INOM en dispatch, och den
/// finns inte: Scaleway-armen registrerar ingen resilience-handler alls, och
/// <c>ScalewayClientRegistration</c> säger också varför ingen får läggas till. (Den mekanism som
/// tidigare stod här — <c>MaxErrorRetry = 0</c> på SES-klienten — raderades med SES-armen i #183.
/// Den som verifierar den här garantin ska läsa registreringen, inte leta efter en SDK-inställning
/// som inte längre finns någonstans i repot.)
/// </para>
/// <para>
/// <b>Undantagskontrakt (ADR 0124, senior-cto-advisor bind 4).</b> En implementation som
/// misslyckas kastar <see cref="Exceptions.EmailDeliveryException"/>, som bär e-postens KIND och
/// det underliggande undantagets TYPNAMN — ingenting annat, och med <c>InnerException</c>
/// avsiktligt TOM. Leverantörens eget undantag får ALDRIG lämna adaptern: ett avslag namnger den
/// mottagare det gäller — hos SES i undantagets meddelande, hos Scaleway i felsvarets kropp, som
/// <c>ScalewayEmailSender</c> därför aldrig läser — många <c>[LoggerMessage]</c>-deklarationer
/// vidarebefordrar ett <see cref="Exception"/>-objekt till sänkan (antalet och dess grep bor i
/// ADR 0124), och <c>Api/Program.cs</c> har ingen generisk <c>catch</c> som stoppar ett
/// omatchat. Ett undantag ÄR
/// en osynlig del av en signatur, så kontraktet står här och inte bara i implementationen.
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
    /// where <c>Email:Provider</c> is unset, as it is in every committed <c>appsettings*.json</c>. Dropping a notification is correct — a missed convenience. Dropping
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
    /// drops every one, the transactional arm sends every one — so a <c>CanDeliver(kind)</c> overload would carry a
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
    /// Sends the login-challenge mail (#1735, ADR 0142 D2): exactly one per admitted request, in the variant
    /// <paramref name="content"/> names.
    /// <para>
    /// <b>Delivery-dependent, and the only login path.</b>
    /// <c>RequestLoginChallengeCommandHandler</c> consults <see cref="CanDeliver"/> as its first statement and
    /// refuses with a 503 before reading anything, so the 503/202 split carries no account information.
    /// Anti-email-bomb is the request path's per-address cooldown and mail budget (<c>IRateBudget</c>,
    /// <c>LoginChallengePolicy</c>), not this port.
    /// </para>
    /// </summary>
    Task SendLoginChallengeAsync(
        string toEmail,
        LoginChallengeEmail content,
        CancellationToken cancellationToken);
}
