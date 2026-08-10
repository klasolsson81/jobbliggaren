using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// #703 — anti-email-bomb cooldown windows for the auth outbound-email surfaces that send to a
/// REQUESTER-chosen address: the registration account-exists notice, the change-email request, and the
/// forgot-password request (#1171). Bound in
/// the Api composition root under <c>Auth:EmailCooldown</c>. The confirmation-link RESEND window keeps its
/// own #733 <see cref="ResendCooldownOptions"/> (the sections stay independent — merging them would break
/// the already-shipped <c>Auth:ResendCooldown</c> config key). Range-guarded + ValidateOnStart so a
/// misconfigured 0/negative TTL fails the host loud rather than silently disabling a security throttle;
/// security-auditor ratifies the values (parity with the IOptions-bound rate limits). Api-only — the
/// cooldown runs in the request path.
/// </summary>
public sealed class AuthEmailCooldownOptions
{
    public const string SectionName = "Auth:EmailCooldown";

    /// <summary>
    /// The flat, non-escalating window (seconds) a single target address waits between register
    /// account-exists notices. Default 60 (parity with the #733 resend window).
    /// </summary>
    [Range(1, 3600)]
    public int AccountExistsNoticeWindowSeconds { get; set; } = 60;

    /// <summary>
    /// The flat, non-escalating window (seconds) applied per-user AND per-target on the change-email
    /// request. Default 60 (parity with the #733 resend window).
    /// </summary>
    [Range(1, 3600)]
    public int ChangeEmailWindowSeconds { get; set; } = 60;

    /// <summary>
    /// The flat, non-escalating window (seconds) a single target address waits between password-reset
    /// links (#1171). Default 60 (parity with the #733 resend window).
    /// <para>
    /// It caps two things at once, and the second is the reason not to raise it casually: the mail
    /// volume an attacker can aim at one inbox, and — since a forgot-password request for an existing
    /// account costs an outbound send that a non-existent one does not — the rate at which a response
    /// timing differential can be sampled. One measurement per address per window.
    /// </para>
    /// </summary>
    [Range(1, 3600)]
    public int PasswordResetWindowSeconds { get; set; } = 60;
}
