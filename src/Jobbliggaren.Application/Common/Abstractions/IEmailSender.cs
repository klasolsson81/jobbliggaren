namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Email-utskick för transactional flows (invitations, waitlist).
/// Impl: ConsoleEmailSender (Infrastructure) — loggar till Serilog/Seq för
/// lokal dev/MVP. Transaktionell mejlväg (SMTP/HTTP-API) är TD för Hetzner-
/// fasen (ADR 0066 — AWS SES borttaget). Templates på svenska per
/// civic-utility-design.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Skickar invitation-email med plaintext-länk till
    /// <c>/registrera?token=&lt;plaintext&gt;</c>. Plaintext-token loggas
    /// aldrig och hashas omedelbart vid mottagning.
    /// </summary>
    Task SendInvitationEmailAsync(
        string toEmail,
        string plaintextToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Bekräftelse-email till anonym besökare som skrev upp sig på väntelistan.
    /// Bekräftar bara att posten är registrerad — säger inget om när/om
    /// approval sker.
    /// </summary>
    Task SendWaitlistConfirmationAsync(
        string toEmail,
        CancellationToken cancellationToken);
}
