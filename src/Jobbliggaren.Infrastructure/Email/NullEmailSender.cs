using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// No-op <see cref="IEmailSender"/> — drops outgoing mail without logging recipient,
/// token, or body. Registered as the fallback for the "Console" provider in any
/// environment that is NOT Development/Test (security-auditor Major #1, Pre-4 STEG 6):
/// <see cref="ConsoleEmailSender"/> writes the recipient email + notification body to
/// <c>ILogger</c>, which becomes durable PII once the persistent Seq sink (TD-104) is
/// attached, so it must never run in a sink-backed, real-recipient environment. A real
/// transactional provider replaces this for beta/prod (TD-101, Hetzner-fas).
///
/// Suppression is logged at Debug WITHOUT any recipient/token so ops can see that mail
/// is being dropped without leaking PII.
/// </summary>
public sealed partial class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        MatchNotificationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        // idempotencyKey is irrelevant to a no-op sender (no transactional provider to dedupe).
        LogSuppressed("match-notification");
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        FollowedCompanyNotificationIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        LogSuppressed("followed-company-notification");
        return Task.CompletedTask;
    }

    [LoggerMessage(3002, LogLevel.Debug,
        "[NullEmailSender] {EmailKind} email suppressed — no transactional provider configured (TD-101)")]
    private partial void LogSuppressed(string emailKind);
}
