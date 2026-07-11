namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// #733 — per-target anti-email-bomb cooldown for the confirmation-link resend endpoint. A resend mints
/// a FRESH token each call, so the Resend idempotency-key dedup (which keys on the token) cannot throttle
/// it, and the per-IP AuthWrite limit protects the attacker's bucket, not the victim inbox. This is the
/// per-address throttle (senior-cto-advisor FORK 1 = A3; the #703 primitive's core). Redis-backed
/// (Infrastructure), flat/non-escalating window — a per-address behavioural variance would itself be an
/// enumeration channel.
/// </summary>
public interface IResendCooldown
{
    /// <summary>
    /// Atomically begins a cooldown window for <paramref name="email"/>: returns <c>true</c> if the
    /// address was NOT in cooldown (and starts a fresh window), <c>false</c> if it still is. The caller
    /// MUST invoke this for EVERY request BEFORE any existence check and treat <c>false</c> as a silent
    /// uniform no-op — the window is thus started existence-independently, so cooldown state never
    /// correlates with whether an account exists (which a second probe could otherwise read via timing).
    /// </summary>
    Task<bool> TryBeginAsync(string email, CancellationToken ct);
}
