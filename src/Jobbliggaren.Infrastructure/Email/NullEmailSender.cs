using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Email;

/// <summary>
/// No-op <see cref="IEmailSender"/> — drops outgoing mail without logging recipient,
/// token, or body. Registered as the fallback for the "Console" provider in any
/// environment that is NOT Development/Test (security-auditor Major #1, Pre-4 STEG 6):
/// <see cref="ConsoleEmailSender"/> writes the recipient email + notification body to
/// <c>ILogger</c> for an RFC 2606/6761-reserved recipient (#1208), which becomes durable PII
/// once the persistent Seq sink (TD-104) is attached, so it must never run in a sink-backed,
/// real-recipient environment. A real
/// transactional provider exists alongside it: ScalewayEmailSender behind Email:Provider=Scaleway
/// (Scaleway Transactional Email, fr-par, #183). This sender is what an UNSET Email:Provider
/// resolves to outside Development/Test, which is the live default today.
///
/// Suppression is logged WITHOUT any recipient/token, and the level is split by consequence:
/// <b>Warning</b> for the four account-lifecycle kinds, <b>Debug</b> for the two notification
/// kinds. security-auditor's minimum named three (<c>email-confirmation</c>,
/// <c>email-changed-notification</c>, <c>account-exists-notice</c>);
/// <c>email-change-confirmation</c> is raised with them for a different reason, stated because it
/// is a deviation from her spec — it is now UNREACHABLE through this sender, since its only caller
/// refuses first, so an occurrence means the gate was bypassed and is more alarming, not less.
/// Until 2026-08-09 all six were Debug, which
/// security-auditor measured as emitting <b>nowhere</b>: <c>Logging:LogLevel:Default</c> is
/// <c>Information</c> in every committed <c>appsettings*.json</c> for both hosts, <c>deploy/</c>
/// sets no override, and this class is registered ONLY outside Development/Test — so the floor,
/// not the uniformity, was the binding constraint, and the sentence claiming ops could see the
/// drop was false in every configuration where the class exists.
/// <b>The payload stays kind-only.</b> Warning reaches a durable sink, so a recipient or token
/// added here later "for debuggability" becomes durable PII (CLAUDE.md §11, #1208). The level is
/// the change; the shape is the invariant.
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
/// <c>Auth:RequireEmailConfirmation</c> is on — the account is created, login is blocked by the
/// <c>EmailConfirmed</c> gate, and the activation link exists nowhere, i.e. a permanently
/// unreachable account. <b>CLOSED at composition time (senior-cto-advisor D1, 2026-08-09):</b>
/// <c>AuthOptionsValidator</c> now refuses to boot outside Development/Test when registrations are
/// open and the registered sender answers <see cref="CanDeliver"/> false — which is this class.
/// The handler is unchanged and needs no <see cref="CanDeliver"/> branch of its own: the
/// configuration that would strand a registrant no longer starts, so <b>for this producer</b> the
/// state is unreachable rather than handled. What the guard does NOT cover, stated so the scope is
/// not read wider than it is: (1) it keys on <c>RegistrationsOpen</c>, so a host with registrations
/// CLOSED boots clean with this sender; (2) an account registered earlier under a delivering
/// provider and still unconfirmed keeps the silent resend path in the next bullet; (3) the allowlist
/// exempts Development and <b>Test</b>, and a reachable <c>ASPNETCORE_ENVIRONMENT=Test</c> host
/// strands registrants exactly as before — <c>release-checklist.md</c> §2.6 point 5.5 counts such a
/// host as a production start and gates it legally, which the technical guard does not; (4) the
/// guard reads a CAPABILITY, not a delivery probe, so a sender answering
/// <see cref="CanDeliver"/> <see langword="true"/> that is nonetheless rejected downstream produces
/// the same stranded account — <c>ScalewayEmailSender</c> answers <see langword="true"/>
/// unconditionally, and the domain publishes DMARC <c>p=reject</c> without <c>rua=</c> (measured
/// 2026-08-08, ADR 0124, cited in <c>AddEmailSender</c>'s Scaleway arm), so a From address outside
/// the verified identity fails silently. Case 4 is owned by #183/#734, never by this gate.</item>
/// <item><c>ResendEmailConfirmationCommandHandler</c> — same stranding, and it must keep returning
/// a uniform 202 for anti-enumeration reasons, so it cannot signal the failure to the caller at
/// all. It no longer writes a <c>User.EmailConfirmationResent</c> audit row for a link that reached
/// nobody.</item>
/// <item><c>ConfirmEmailChangeCommandHandler</c>'s old-address notice — an OWASP ASVS V2.5 /
/// NIST SP 800-63B breach-detection control. Deliberately NOT refused (that would fail a completed,
/// legitimate change), so with this sender the control is silently off. <b>security-auditor ruled
/// that acceptable on 2026-08-09, on trigger-unreachability rather than on a launch condition:</b>
/// the only mint site (<c>ChangeEmailCommandHandler</c>) is now behind <see cref="CanDeliver"/>, so
/// while this sender is registered no token can exist and the event the control detects cannot
/// occur. Control and guarded flow go dark together, and both return when the provider is set —
/// no checklist item, nothing to remember. Residual, stated so it is not rediscovered: a token
/// minted under a capable sender and confirmed after an operator swaps to this one, bounded by the
/// 24h token lifespan, with C6 logout-everywhere as the previous owner's crude remaining signal.</item>
/// <item><c>RequestPasswordResetCommandHandler</c> (#1171) — the password changes only when the
/// emailed link is opened, so a dropped send leaves someone who has already lost access with no way
/// back in. It consults <see cref="CanDeliver"/> and refuses (503), like change-email. <b>The check
/// is the handler's FIRST statement, and that position is the anti-enumeration property, not
/// tidiness:</b> the surface is unauthenticated and answers a uniform 202, so a capability check
/// placed after the account lookup would be reachable only when an account exists and the 503 would
/// itself disclose existence.</item>
/// <item><c>ResetPasswordCommand</c>'s password-changed notice (#1171) — the same OWASP ASVS V2.5 /
/// NIST SP 800-63B breach-detection control as the old-address notice above, and closed by the same
/// argument rather than by a new gate: no reset token can be minted while this sender is registered,
/// so the event the control reports cannot occur. Control and guarded flow go dark together. It
/// carries the narrower residual too — a token minted under a capable sender and redeemed after an
/// operator swaps to this one — bounded by the reset lifespan, which is
/// <c>PasswordResetTokenProviderOptions.LifespanMinutes</c> rather than the 24h above.</item>
/// </list>
/// </para>
/// <para>
/// <b>One consequence for the notification callers, stated rather than left to be discovered.</b>
/// All three call <c>MarkSent(clock)</c> after this sender returns — <c>BackgroundMatchingJob</c>,
/// and <c>DigestDispatchJob</c> on both its match and its followed-company path — so
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
        LogSuppressedNotification("match-notification");
        return Task.CompletedTask;
    }

    public Task SendFollowedCompanyNotificationEmailAsync(
        string toEmail,
        FollowedCompanyNotificationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressedNotification("followed-company-notification");
        return Task.CompletedTask;
    }

    public Task SendEmailChangeConfirmationAsync(
        string toEmail,
        EmailChangeConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("email-change-confirmation");
        return Task.CompletedTask;
    }

    public Task SendEmailChangedNotificationAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("email-changed-notification");
        return Task.CompletedTask;
    }

    public Task SendEmailConfirmationAsync(
        string toEmail,
        EmailConfirmationEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("email-confirmation");
        return Task.CompletedTask;
    }

    public Task SendAccountExistsNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("account-exists-notice");
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string toEmail,
        PasswordResetEmail content,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("password-reset");
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedNoticeAsync(
        string toEmail,
        CancellationToken cancellationToken)
    {
        LogSuppressedConsequential("password-changed-notice");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A dropped convenience. Debug is correct here and is NOT the defect security-auditor measured:
    /// nobody is stranded or blinded by a missed notification, so this one may stay below the floor.
    /// </summary>
    [LoggerMessage(3002, LogLevel.Debug,
        "[NullEmailSender] {EmailKind} email suppressed — no transactional provider configured")]
    private partial void LogSuppressedNotification(string emailKind);

    /// <summary>
    /// A drop that strands a person or blinds a security control. Warning, because the whole point
    /// is that an operator sees it, and Debug is filtered out in every environment where this class
    /// is registered. <b>Kind only — never a recipient, address or token</b>: this level reaches a
    /// durable sink (CLAUDE.md §11, #1208).
    /// </summary>
    /// <remarks>
    /// The message names the CONSEQUENCE, not the caller, and that is a correction rather than a
    /// style choice: an earlier draft ended "this send was required for the caller to complete",
    /// which both reviewers measured false for every kind that can actually emit this line — all
    /// four callers return success anyway. It was true only of <c>email-change-confirmation</c>,
    /// the one kind that cannot reach here. This is the string an on-call engineer reads at 03:00;
    /// pointing it at a failed call that never failed sends them looking for the wrong thing.
    /// </remarks>
    [LoggerMessage(3007, LogLevel.Warning,
        "[NullEmailSender] {EmailKind} email suppressed — no transactional provider configured; "
        + "a recipient is stranded or a security notice is lost")]
    private partial void LogSuppressedConsequential(string emailKind);
}
