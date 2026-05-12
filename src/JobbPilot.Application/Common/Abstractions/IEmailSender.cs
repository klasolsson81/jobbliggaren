namespace JobbPilot.Application.Common.Abstractions;

/// <summary>
/// Email-utskick för transactional flows (invitations, waitlist).
/// Impl: SesEmailSender (Infrastructure, F2-P0d) — AWS SES eu-north-1.
/// Templates på svenska per civic-utility-design.
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
