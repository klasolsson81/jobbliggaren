namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Check-and-set anti-abuse cooldown gate (generalised from the #733 resend primitive; #703). A single
/// call TESTS and STARTS a cooldown window for a <c>(scope, subject)</c> pair: returns <c>true</c> if the
/// pair was NOT in cooldown (a fresh window is started), <c>false</c> if it still is. Redis-backed
/// (Infrastructure), flat / non-escalating window — a per-subject behavioural variance would itself be an
/// enumeration channel.
/// <para>
/// The gate is POLICY-FREE by design: it returns a <c>bool</c> and takes the <paramref name="window"/> as
/// a parameter, so the CALLER owns the policy. An anti-enumeration caller (the unauthenticated resend and
/// account-exists-notice paths) MUST call this BEFORE any existence check and treat <c>false</c> as a
/// SILENT uniform no-op, so cooldown state never correlates with account existence; an authenticated caller
/// (change-email) MAY surface <c>false</c> as a visible error. <paramref name="scope"/> namespaces the
/// window so distinct actions never collide; <paramref name="subject"/> (an email address or a user id) is
/// normalised (trim + lower-invariant) and SHA-256-hashed by the implementation — the raw value is never
/// written to Redis.
/// </para>
/// </summary>
public interface ICooldownGate
{
    /// <summary>
    /// Atomically begins a cooldown <paramref name="window"/> for the <paramref name="scope"/>+<paramref
    /// name="subject"/> pair: returns <c>true</c> if it was NOT in cooldown (and starts a fresh window),
    /// <c>false</c> if it still is (no window is (re)started). The caller decides whether a <c>false</c> is
    /// a silent uniform no-op (anti-enumeration) or a visible error (authenticated path).
    /// </summary>
    Task<bool> TryBeginAsync(string scope, string subject, TimeSpan window, CancellationToken ct);
}
