using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Transactional <see cref="IEmailSender"/> over the Amazon SES v2 HTTPS API in
/// <c>eu-north-1</c> (ADR 0124, #1237). Registered ONLY when <c>Email:Provider="Ses"</c>
/// (the DI switch in <c>AddEmailSender</c>). The SDK types
/// (<see cref="IAmazonSimpleEmailServiceV2"/>, <see cref="SendEmailRequest"/>) stay in
/// Infrastructure and NEVER cross the <see cref="IEmailSender"/> port — parity
/// Refit/PdfPig/QuestPDF, and pinned by a NetArchTest fact in <c>NoAmazonReferenceTests</c>.
/// <para>
/// <b>HTTPS API, never SMTP.</b> Netcup drops outbound 25/465/587 (`Mail block`), and
/// ADR 0050 §10 ratified the HTTPS-API choice before this arm existed. Do not add an SMTP
/// fallback and do not ask the host to open 587.
/// </para>
/// <para>
/// <b>PII discipline (CLAUDE.md §5).</b> No recipient address, token, body, subject or credential
/// is ever logged — only the email kind and, on failure, the exception TYPE name. AWS exception
/// MESSAGES can embed request context, so <c>ex.Message</c>/<c>ex.ToString()</c> deliberately
/// never reach the logger. The response's <c>MessageId</c> is not captured either: it is a
/// provider correlation id that joins to a recipient inside the SES console, so surfacing it is a
/// security-auditor question, not a default.
/// </para>
/// <para>
/// <b>A note for whoever restores "missing" telemetry.</b> The historical SES sender deleted in
/// 2026-06 logged <c>To</c>, <c>Subject</c> and <c>MessageId</c> at Information. That is a PII
/// regression under today's rules and is durable once the Seq sink is attached. It is absent on
/// purpose.
/// </para>
/// <para>
/// <b>No idempotency parameter.</b> SES v2 <c>SendEmail</c> offers none, and the port stopped
/// carrying one in ADR 0124 — dedupe across calls is owned by the claim-then-send spine and by
/// <c>ICooldownGate</c> (ADR 0103), one layer up. The residual intra-dispatch transport retry is
/// closed by <c>MaxErrorRetry = 0</c> in <see cref="SesClientRegistration"/>.
/// </para>
/// </summary>
public sealed partial class SesEmailSender(
    IAmazonSimpleEmailServiceV2 ses,
    IOptions<EmailOptions> options,
    ILogger<SesEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// SES requires an explicit charset per content field. Resend inferred it; SES does not, and
    /// without this åäö arrive mojibaked (CLAUDE.md §10 — "UTF-8 everywhere, åäö must survive
    /// serialization").
    /// </summary>
    private const string Utf8 = "UTF-8";

    private readonly EmailOptions _options = options.Value;

    /// <summary>
    /// <see langword="true"/> — this is the real transactional path. Reaching this class at all
    /// already means <c>AddEmailSender</c> accepted an explicit <c>Email:Provider=Ses</c> together
    /// with a region and both credentials, fail-loud at REGISTRATION time; the arm cannot be entered
    /// half-configured. See <see cref="IEmailSender.CanDeliver"/>.
    /// <para>
    /// A transport failure after that is a different question from capability and is NOT modelled
    /// here: it surfaces as <c>EmailDeliveryException</c> from the send itself. This property answers
    /// "is delivery possible at all", never "will this particular message arrive".
    /// </para>
    /// </summary>
    public bool CanDeliver => true;

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.MatchNotification(_options.BaseUrl, content),
            "match-notification",
            cancellationToken);

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail, FollowedCompanyNotificationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.FollowedCompanyNotification(_options.BaseUrl, content),
            "followed-company-notification",
            cancellationToken);

    public Task SendEmailChangeConfirmationAsync(
        string toEmail, EmailChangeConfirmationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailChangeConfirmation(_options.BaseUrl, content),
            "email-change-confirmation",
            cancellationToken);

    public Task SendEmailChangedNotificationAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailChangedNotification(_options.BaseUrl),
            "email-changed-notification",
            cancellationToken);

    public Task SendEmailConfirmationAsync(
        string toEmail, EmailConfirmationEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.EmailConfirmation(_options.BaseUrl, content),
            "email-confirmation",
            cancellationToken);

    public Task SendAccountExistsNoticeAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.AccountExistsNotice(_options.BaseUrl),
            "account-exists-notice",
            cancellationToken);

    public Task SendPasswordResetAsync(
        string toEmail, PasswordResetEmail content, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.PasswordReset(_options.BaseUrl, content),
            "password-reset",
            cancellationToken);

    public Task SendPasswordChangedNoticeAsync(
        string toEmail, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            EmailTemplates.PasswordChangedNotice(_options.BaseUrl),
            "password-changed-notice",
            cancellationToken);

    private async Task SendAsync(
        string toEmail,
        EmailTemplates.EmailContent body,
        string emailKind,
        CancellationToken cancellationToken)
    {
        // The try starts here, not at the SDK call: everything that touches `toEmail` or the
        // rendered body belongs inside the boundary. The line is the THREAT MODEL, not throw-site
        // arithmetic — this adapter contains a THIRD PARTY's exceptions, whose messages we neither
        // write nor control. Our own code above (EmailTemplates) is outside it for the same reason,
        // not because it happens to have no throw sites today.
        try
        {
            var request = new SendEmailRequest
            {
                FromEmailAddress = $"{_options.FromName} <{_options.FromAddress}>",

                // AWS SDK v4 leaves request collections NULL by default (v4 migration guide), so these
                // are ASSIGNED, never .Add()-ed onto an assumed-empty list. AWSConfigs.InitializeCollections
                // is deliberately not set — it is process-global and buys nothing here.
                Destination = new Destination { ToAddresses = [toEmail] },
                Content = new EmailContent
                {
                    Simple = new Message
                    {
                        Subject = new Content { Data = body.Subject, Charset = Utf8 },
                        Body = new Body { Text = new Content { Data = body.PlainTextBody, Charset = Utf8 } },
                    },
                },
            };

            await ses.SendEmailAsync(request, cancellationToken);
            LogSent(emailKind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log WITHOUT recipient/body (PII) and without the exception message.
            LogFailed(emailKind, ex.GetType().Name);

            // The provider's exception does not leave this adapter: AWS puts the recipient address
            // in its error messages. `ex` becomes a TYPE NAME, never an InnerException. Why
            // containment rather than patching the log sites, and why the port declares this
            // contract: ADR 0124.
            //
            // Cancellation is EXCLUDED by the `when` above. Without it a TaskCanceledException
            // became an EmailDeliveryException — not an OperationCanceledException — so the
            // callers' own cancellation filters matched and swallowed a host shutdown as one
            // user's send failure. It does not reopen the containment: an OCE is raised by the
            // cancellation machinery, never by an SES response.
            throw new EmailDeliveryException(emailKind, ex.GetType().Name);
        }
    }

    [LoggerMessage(3005, LogLevel.Information, "[SesEmailSender] {EmailKind} email sent")]
    private partial void LogSent(string emailKind);

    [LoggerMessage(3006, LogLevel.Error,
        "[SesEmailSender] {EmailKind} email FAILED ({ErrorType}) — no recipient/body logged")]
    private partial void LogFailed(string emailKind, string errorType);
}
