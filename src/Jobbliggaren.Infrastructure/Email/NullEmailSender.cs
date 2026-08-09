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
/// <b>Whom this is a valid substitute for, and whom it is NOT (#1087, AC 6).</b>
/// <see cref="CanDeliver"/> is <see langword="false"/>, so it is a valid <see cref="IEmailSender"/>
/// for a caller whose success does NOT depend on delivery, and the contract now requires a
/// delivery-dependent caller to consult the property and refuse up front. Before that member
/// existed this class was an LSP violation — the two caller kinds were indistinguishable, so
/// <c>ChangeEmailCommandHandler</c> reported a completed action that could not occur.
/// </para>
/// <para>
/// <b>VALID for</b> the three notification call sites (<c>BackgroundMatchingJob</c>,
/// <c>DigestDispatchJob</c> ×2) and for <c>RegisterCommandHandler</c>'s account-exists notice, which
/// is informational and strands nobody by its absence.
/// </para>
/// <para>
/// <b>NOT a valid substitute for</b> — enumerated because each one is a real hazard, not a style
/// preference:
/// <list type="bullet">
/// <item><c>ChangeEmailCommandHandler</c> — the address is swapped only when the emailed link is
/// opened, so a dropped send is an unfinishable request. It now consults
/// <see cref="CanDeliver"/> and refuses (503).</item>
/// <item><c>RegisterCommandHandler</c>'s confirmation send, when
/// <c>Auth:RequireEmailConfirmation</c> is on. <b>This one is still open and is the worse
/// case:</b> the account is created, login is blocked by the <c>EmailConfirmed</c> gate, and the
/// activation link exists nowhere — a permanently unreachable account. The combination is
/// reachable, not hypothetical: <c>AuthOptionsValidator</c> forces that flag on whenever
/// <c>Auth:RegistrationsOpen</c> is true outside Development/Test. Owned by the composition-time
/// boot guard (senior-cto-advisor D1, 2026-08-09), and anchored as a condition on
/// <c>release-checklist.md</c> §2.6 point 5.5 rather than left in an issue body — the trigger there
/// is already exactly that configuration.</item>
/// <item><c>ResendEmailConfirmationCommandHandler</c> — same stranding, and it must keep returning
/// a uniform 202 for anti-enumeration reasons, so it cannot signal the failure to the caller at
/// all. It no longer writes a <c>User.EmailConfirmationResent</c> audit row for a link that reached
/// nobody.</item>
/// <item><c>ConfirmEmailChangeCommandHandler</c>'s old-address notice — an OWASP ASVS V2.5 /
/// NIST SP 800-63B breach-detection control. Deliberately NOT refused (that would fail a completed,
/// legitimate change), so with this sender the control is silently off. Whether that is acceptable
/// is <c>security-auditor</c>'s call, not this comment's.</item>
/// </list>
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
