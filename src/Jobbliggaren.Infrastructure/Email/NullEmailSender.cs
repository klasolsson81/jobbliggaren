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
/// transactional provider exists alongside it: SesEmailSender behind Email:Provider=Ses
/// (Amazon SES v2, eu-north-1, ADR 0124). This sender is what an UNSET Email:Provider
/// resolves to outside Development/Test, which is the live default today.
///
/// Suppression is logged at Debug WITHOUT any recipient/token so ops can see that mail
/// is being dropped without leaking PII. <b>The level is uniform across all six kinds, and
/// that is currently unexamined rather than decided</b> — a dropped ASVS V2.5 old-address
/// security notice and a dropped background-match notification are the same Debug line.
/// dotnet-architect raised the split as Nice-to-have (2026-08-09) and routed the question
/// of whether dropping the security notice is acceptable at all to security-auditor; the
/// level follows her verdict, not this comment.
///
/// <para>
/// <b>Whom this is a valid substitute for (#1087, AC 6).</b> <see cref="CanDeliver"/> is
/// <see langword="false"/>, and that is the whole answer: it is a valid <see cref="IEmailSender"/>
/// for a caller whose success does NOT depend on delivery, and the contract now requires a
/// delivery-dependent caller to consult the property and refuse up front. Before that member
/// existed this class was an LSP violation — the two caller kinds were indistinguishable, so
/// <c>ChangeEmailCommandHandler</c> reported a completed action that could not occur.
/// </para>
/// <para>
/// <b>One consequence for the notification callers, stated rather than left to be discovered.</b>
/// <c>BackgroundMatchingJob</c> calls <c>match.MarkSent(clock)</c> after this sender returns, so
/// the claim-then-send spine records rows as <c>Sent</c> for mail that was never sent. That is
/// deliberate and defensible — the port call did succeed, and the state machine tracks DISPATCH,
/// not delivery — but a reader of <c>NotificationStatus.Sent</c> should know it does not mean an
/// inbox received anything while this sender is registered.
/// </para>
/// </summary>
public sealed partial class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    /// <summary>
    /// Always <see langword="false"/> — this sender delivers nothing, by design. See
    /// <see cref="IEmailSender.CanDeliver"/> for the contract this answers.
    /// </summary>
    public bool CanDeliver => false;

    public Task SendMatchNotificationEmailAsync(
        string toEmail,
        MatchNotificationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressed("match-notification");
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressed("followed-company-notification");
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressed("email-change-confirmation");
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        LogSuppressed("email-changed-notification");
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressed("email-confirmation");
        return Task.CompletedTask;
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        LogSuppressed("account-exists-notice");
        return Task.CompletedTask;
    }

    [LoggerMessage(3002, LogLevel.Debug,
        "[NullEmailSender] {EmailKind} email suppressed — no transactional provider configured")]
    private partial void LogSuppressed(string emailKind);
}
