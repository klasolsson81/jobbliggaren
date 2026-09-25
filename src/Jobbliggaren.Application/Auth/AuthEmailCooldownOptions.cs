using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// #703 — anti-email-bomb cooldown windows for the auth outbound-email surfaces that send to a
/// REQUESTER-chosen address: the change-email request and the login-challenge request (#1735). Bound in
/// the Api composition root under <c>Auth:EmailCooldown</c>. Range-guarded + ValidateOnStart so a
/// misconfigured 0/negative TTL fails the host loud rather than silently disabling a security throttle;
/// security-auditor ratifies the values (parity with the IOptions-bound rate limits). Api-only — the
/// cooldown runs in the request path.
/// </summary>
public sealed class AuthEmailCooldownOptions
{
    public const string SectionName = "Auth:EmailCooldown";

    /// <summary>
    /// The flat, non-escalating window (seconds) applied per-user AND per-target on the change-email
    /// request. Default 60.
    /// </summary>
    [Range(1, 3600)]
    public int ChangeEmailWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Per-TARGET silent window on the login-challenge request (#1735): one mint per address per window.
    /// Default 60, the same as the change-email window. Configuration rather than a
    /// <c>LoginChallengePolicy</c> constant because it does not enter the guess arithmetic: the mail and
    /// code budgets cap mints whatever the window is (senior-cto-advisor Q3, accepted by security-auditor).
    /// </summary>
    [Range(1, 3600)]
    public int LoginChallengeWindowSeconds { get; set; } = 60;
}
