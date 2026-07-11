using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// #733 — per-target confirmation-link resend cooldown (Application-owned contract, bound in the Api
/// composition root under the <c>Auth:ResendCooldown</c> section). The flat, non-escalating window a
/// single address must wait between resend sends; a within-window repeat is a silent uniform no-op
/// (anti-enumeration). security-auditor ratifies the value (parity with the IOptions-bound rate limits).
/// Api-only — the cooldown runs in the request path; the Worker does not need it.
/// </summary>
public sealed class ResendCooldownOptions
{
    public const string SectionName = "Auth:ResendCooldown";

    /// <summary>
    /// The cooldown window in seconds. Default 60 (Klas decision, 2026-07-10). Range-guarded +
    /// ValidateOnStart so a misconfigured 0/negative TTL fails the host loud rather than silently
    /// disabling the anti-email-bomb throttle (a security invariant).
    /// </summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
