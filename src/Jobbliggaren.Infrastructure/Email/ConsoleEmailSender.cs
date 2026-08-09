using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Dev/MVP-impl av IEmailSender. Skriver email-innehåll till ILogger istället
/// för att skicka via riktig mailserver. Registreras BARA i Development/Test
/// (<c>AddEmailSender</c>); i övriga miljöer faller "Console" tillbaka på
/// <see cref="NullEmailSender"/>.
///
/// Den riktiga transaktionella mejlvägen ÄR byggd och lever bredvid den här:
/// <see cref="SesEmailSender"/> bakom <c>Email:Provider=Ses</c> (Amazon SES v2 i
/// eu-north-1, ADR 0124, #1207).
///
/// Säkerhet: plaintext-tokens skrivs till logs här, vilket är acceptabelt för
/// dev men ALDRIG i prod. Sedan 2026-08-04 är dev-Seq admin-autentiserad (#1198),
/// men sänkan bär fortfarande hela mejlkroppen och ingen kadens ommäter det —
/// [#1208](https://github.com/klasolsson81/jobbliggaren/issues/1208) äger den luckan.
/// </summary>
public sealed partial class ConsoleEmailSender(
    ILogger<ConsoleEmailSender> logger,
    IOptions<EmailOptions> options)
    : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>
    /// <see langword="true"/>, and that is a substantive answer rather than a convenience for the
    /// test suite (#1087). This sender writes the whole body — activation and confirmation links
    /// included — to <c>ILogger</c>, so a developer CAN complete a token→email→confirm flow from the
    /// log. Delivery-dependent handlers must therefore work in Development exactly as they will in
    /// production; answering <see langword="false"/> here would refuse the very flows dev exists to
    /// exercise. See <see cref="IEmailSender.CanDeliver"/>.
    /// </summary>
    public bool CanDeliver => true;

    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.MatchNotification(_options.BaseUrl, content);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.FollowedCompanyNotification(_options.BaseUrl, content);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        // The confirmation link (with the plaintext token) is written to the log here — acceptable in
        // Dev/Test (this sender is Dev/Test-only; NullEmailSender is the non-dev fallback), NEVER in
        // prod. Read the link out of the console/Seq log to complete the flow locally.
        var body = EmailTemplates.EmailChangeConfirmation(_options.BaseUrl, content);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.EmailChangedNotification(_options.BaseUrl);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        // The activation link (with the plaintext token) is written to the log here — acceptable in
        // Dev/Test (this sender is Dev/Test-only; NullEmailSender is the non-dev fallback), NEVER in
        // prod. Read the link out of the console/Seq log to complete the flow locally.
        var body = EmailTemplates.EmailConfirmation(_options.BaseUrl, content);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        var body = EmailTemplates.AccountExistsNotice(_options.BaseUrl);
        LogEmail(toEmail, body.Subject, body.PlainTextBody);
        return Task.CompletedTask;
    }

    [LoggerMessage(3001, LogLevel.Information,
        "[ConsoleEmailSender] To={To} Subject={Subject}\n---\n{Body}\n---")]
    private partial void LogEmail(string to, string subject, string body);
}
