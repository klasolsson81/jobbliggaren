using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Transaktionell <see cref="IEmailSender"/> via Resend (ADR 0080 Vag 4 PR-4 — löser
/// TD-101). Registreras BARA när <c>Email:Provider="Resend"</c> (DI-switch i
/// <c>AddEmailSender</c>). Den officiella Resend-SDK:n wrappar
/// <c>IHttpClientFactory</c>; <c>IResend</c>/<c>EmailMessage</c> stannar i Infrastructure
/// och korsar ALDRIG <see cref="IEmailSender"/>-porten (paritet PdfPig/QuestPDF/Refit).
/// <para>
/// <b>PII-disciplin (CLAUDE.md §5):</b> ingen mottagar-adress, token eller body loggas —
/// bara email-kind + ev. fel-typ. Mallarna (<see cref="EmailTemplates"/>) är icke-PII och
/// bär en obligatorisk inställnings-/avregistreringslänk.
/// </para>
/// <para>
/// <b>GDPR:</b> Resend är en US-processor → utskick till riktiga användare är en
/// tredjelandsöverföring som kräver DPA/SCC + security-auditor-sign-off FÖRE non-dev-flippen
/// (CTO 2026-06-24). I dev = test-mode (from <c>onboarding@resend.dev</c>; free-tier levererar
/// bara till kontoägarens egen e-post). Icke-dev defaultar fortfarande till NullEmailSender
/// tills providern explicit konfigureras.
/// </para>
/// </summary>
public sealed partial class ResendEmailSender(
    IResend resend,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
    {
        // Fail loud on a missing key (e.g. a default-constructed struct, whose Value is null) rather
        // than silently sending non-idempotently — the match-notification path MUST be idempotent
        // (#187). It is the only retry-bearing send (the nightly scan + digest jobs).
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.MatchNotification(_options.BaseUrl, content),
            "match-notification",
            idempotencyKey.Value,
            cancellationToken);
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail, FollowedCompanyNotificationEmail content,
        FollowedCompanyNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
    {
        // Fail loud on a missing key rather than silently sending non-idempotently — the
        // company-follow digest is a retry-bearing send (ADR 0087 D5, parity the match path).
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.FollowedCompanyNotification(_options.BaseUrl, content),
            "followed-company-notification",
            idempotencyKey.Value,
            cancellationToken);
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail, EmailChangeConfirmationEmail content,
        EmailChangeConfirmationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
    {
        // Fail loud on a missing key (default-constructed struct) rather than silently sending
        // non-idempotently — a transport retry must not double-deliver the confirmation.
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.EmailChangeConfirmation(_options.BaseUrl, content),
            "email-change-confirmation",
            idempotencyKey.Value,
            cancellationToken);
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail, EmailChangedNotificationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.EmailChangedNotification(_options.BaseUrl),
            "email-changed-notification",
            idempotencyKey.Value,
            cancellationToken);
    }

    public Task SendEmailConfirmationAsync(
        string toEmail, EmailConfirmationEmail content,
        EmailConfirmationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
    {
        // Fail loud on a missing key (default-constructed struct) rather than silently sending
        // non-idempotently — a transport retry must not double-deliver the confirmation link.
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.EmailConfirmation(_options.BaseUrl, content),
            "email-confirmation",
            idempotencyKey.Value,
            cancellationToken);
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail, AccountExistsNoticeIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Fail loud on a missing key; the notice key also dedupes repeated attempts on the same taken
        // address within Resend's window (anti-email-bomb, CTO-bind Risk 6).
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.AccountExistsNotice(_options.BaseUrl),
            "account-exists-notice",
            idempotencyKey.Value,
            cancellationToken);
    }

    private async Task SendAsync(
        string toEmail, EmailTemplates.EmailContent body, string emailKind,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var message = new EmailMessage
        {
            From = $"{_options.FromName} <{_options.FromAddress}>",
            Subject = body.Subject,
            TextBody = body.PlainTextBody,
        };
        message.To.Add(toEmail);

        try
        {
            // The idempotency overload makes a transport-level retry (SDK/HttpClient) of the SAME
            // logical send a no-op at Resend. The key is NEVER logged (PII-free but unnecessary;
            // PII-discipline §5).
            await resend.EmailSendAsync(idempotencyKey, message, cancellationToken);
            LogSent(emailKind);
        }
        catch (Exception ex)
        {
            // Logga UTAN mottagare/body (PII/credential). Felet bubblar upp —
            // Api-pipelinen / dispatch-jobbets per-user-isolering avgör hantering.
            LogFailed(emailKind, ex.GetType().Name);
            throw;
        }
    }

    [LoggerMessage(3003, LogLevel.Information, "[ResendEmailSender] {EmailKind} email sent")]
    private partial void LogSent(string emailKind);

    [LoggerMessage(3004, LogLevel.Error,
        "[ResendEmailSender] {EmailKind} email FAILED ({ErrorType}) — no recipient/body logged")]
    private partial void LogFailed(string emailKind, string errorType);
}
