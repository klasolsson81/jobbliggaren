using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// #1171 — bounds the out-of-band forgot-password dispatch queue. Bound in the Api composition root
/// under <c>Auth:PasswordResetDispatch</c> with <c>ValidateDataAnnotations().ValidateOnStart()</c>, so a
/// misconfigured capacity fails the host loud rather than silently disabling a control (parity with
/// <see cref="AuthEmailCooldownOptions"/>).
/// </summary>
public sealed class PasswordResetDispatchOptions
{
    public const string SectionName = "Auth:PasswordResetDispatch";

    /// <summary>
    /// How many queued requests are held before further ones are dropped.
    /// <para>
    /// <b>Bounded rather than unbounded, and that is a security decision, not tuning.</b> The producer
    /// is an UNAUTHENTICATED endpoint whose per-IP rate limit parallelises trivially, so an unbounded
    /// channel is a memory-exhaustion surface. The repo has already taken this position once:
    /// <c>RateLimitingExtensions</c> sets <c>QueueLimit = 0</c> on every policy, with the reason written
    /// beside it (queue-memory exhaustion plus a latency spike that masks the attack signal).
    /// </para>
    /// <para>
    /// Default 1000, derived rather than picked: one send is a provider round trip of roughly
    /// 100-300 ms, so a single consumer drains 3-10 per second and 1000 is a two-to-five minute
    /// backlog. Far above any legitimate burst — the per-address cooldown caps that hard — and low
    /// enough that a flood raises the full-queue warning within minutes instead of accumulating for an
    /// hour. Bound as configuration precisely so disagreeing with the number costs one line (senior-cto-advisor,
    /// 2026-08-10).
    /// </para>
    /// </summary>
    [Range(1, 100_000)]
    public int Capacity { get; set; } = 1000;
}
