using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Dev/MVP-impl av IEmailSender. Skriver email-innehåll till ILogger istället
/// för att skicka via riktig mailserver. Räcker för Fas 2-MVP (Klas kontrollerar
/// både utfärdande och mottagare via klasskamrat-tester). Riktig transaktionell
/// mejlväg (SMTP/HTTP-API) är TD för Hetzner-fasen (ADR 0066 — AWS SES borttaget).
///
/// Säkerhet: plaintext-tokens skrivs till logs här, vilket är acceptabelt för
/// dev men ALDRIG i prod. När en riktig mejl-provider återinförs registreras den
/// via EmailOptions.Provider-switchen istället för denna sender.
/// </summary>
public sealed partial class ConsoleEmailSender(
    ILogger<ConsoleEmailSender> logger,
    IOptions<EmailOptions> options)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public Task SendInvitationEmailAsync(
        string toEmail,
        string plaintextToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var content = EmailTemplates.InvitationEmail(_options.BaseUrl, plaintextToken, expiresAt);
        LogEmail(toEmail, content.Subject, content.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendWaitlistConfirmationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var content = EmailTemplates.WaitlistConfirmationEmail();
        LogEmail(toEmail, content.Subject, content.PlainTextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(3001, LogLevel.Information,
        "[ConsoleEmailSender] To={To} Subject={Subject}\n---\n{Body}\n---")]
    private partial void LogEmail(string to, string subject, string body);
}
