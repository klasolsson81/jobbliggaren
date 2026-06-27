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
}
