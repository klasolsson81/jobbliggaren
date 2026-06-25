using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resend;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// Transaktionell <see cref="IEmailSender"/> via Resend (ADR 0080 Vag 4 PR-4 — löser
/// TD-101). Registreras BARA när <c>Email:Provider="Resend"</c> (DI-switch i
/// <c>AddInvitationsAndEmail</c>). Den officiella Resend-SDK:n wrappar
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

    public Task SendInvitationEmailAsync(
        string toEmail, string plaintextToken, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        => SendAsync(
            toEmail,
            EmailTemplates.InvitationEmail(_options.BaseUrl, plaintextToken, expiresAt),
            "invitation",
            idempotencyKey: null,
            cancellationToken);

    public Task SendWaitlistConfirmationAsync(string toEmail, CancellationToken cancellationToken)
        => SendAsync(
            toEmail,
            EmailTemplates.WaitlistConfirmationEmail(),
            "waitlist-confirmation",
            idempotencyKey: null,
            cancellationToken);

    public Task SendMatchNotificationEmailAsync(
        string toEmail, MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey, CancellationToken cancellationToken)
    {
        // Fail loud on a missing key (e.g. a default-constructed struct, whose Value is null) rather
        // than silently falling back to the non-idempotent overload — the match-notification path
        // MUST be idempotent (#187). The match path is the only retry-bearing send (the nightly scan
        // + digest jobs); invitation/waitlist are user-initiated with no retry loop, so they pass no
        // key (ADR 0080 PR-4 item 4, CTO scope-bind 2026-06-25).
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey.Value);
        return SendAsync(
            toEmail,
            EmailTemplates.MatchNotification(_options.BaseUrl, content),
            "match-notification",
            idempotencyKey.Value,
            cancellationToken);
    }

    private async Task SendAsync(
        string toEmail, EmailTemplates.EmailContent body, string emailKind,
        string? idempotencyKey, CancellationToken cancellationToken)
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
            // logical send a no-op at Resend; the non-idempotent paths keep the plain overload. The
            // key is NEVER logged (it is PII-free but unnecessary in the log; PII-discipline §5).
            if (idempotencyKey is null)
                await resend.EmailSendAsync(message, cancellationToken);
            else
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
