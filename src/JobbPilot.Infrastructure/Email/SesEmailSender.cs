using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using JobbPilot.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Email;

/// <summary>
/// AWS SES v2-baserad email-sender. Aktiveras via
/// <c>EmailOptions.Provider="Ses"</c>. Kräver att <see cref="IAmazonSimpleEmailServiceV2"/>
/// är registrerad i DI (DependencyInjection.AddInvitationsAndEmail).
///
/// Initialt drift i SES sandbox-mode: bara verifierade mottagar-emails accepteras.
/// För klasskamrat-tester verifierar Klas mottagarnas adresser manuellt i
/// SES-konsolen. Production access (no-sandbox) ansöks om innan public launch
/// per ADR 0005 amendment 2026-05-12.
/// </summary>
public sealed partial class SesEmailSender(
    IAmazonSimpleEmailServiceV2 ses,
    ILogger<SesEmailSender> logger,
    IOptions<EmailOptions> options)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendInvitationEmailAsync(
        string toEmail,
        string plaintextToken,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var content = EmailTemplates.InvitationEmail(_options.BaseUrl, plaintextToken, expiresAt);
        await SendAsync(toEmail, content.Subject, content.PlainTextBody, cancellationToken);
    }

    public async Task SendWaitlistConfirmationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var content = EmailTemplates.WaitlistConfirmationEmail();
        await SendAsync(toEmail, content.Subject, content.PlainTextBody, cancellationToken);
    }

    private async Task SendAsync(
        string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = $"{_options.FromName} <{_options.FromAddress}>",
            Destination = new Destination { ToAddresses = [toEmail] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Text = new Content { Data = body, Charset = "UTF-8" },
                    },
                },
            },
        };

        var response = await ses.SendEmailAsync(request, cancellationToken);
        LogSent(toEmail, subject, response.MessageId);
    }

    [LoggerMessage(3002, LogLevel.Information,
        "[SesEmailSender] Sent To={To} Subject={Subject} MessageId={MessageId}")]
    private partial void LogSent(string to, string subject, string messageId);
}
