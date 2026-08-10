namespace Jobbliggaren.Application.Auth;

/// <summary>
/// One queued forgot-password request (#1171). Carries the SUBMITTED address — no user id, and no
/// token: at this point neither exists, because the account lookup and the token mint both happen on
/// the consumer side. That is the whole point of the shape, and it is what keeps a bearer credential
/// off every durable surface.
/// </summary>
/// <param name="Email">
/// The address as submitted. Never logged, and never compared against anything before the consumer's
/// lookup — the request path must not learn whether it resolves.
/// </param>
/// <param name="IpAddress">
/// The ANONYMISED client IP, captured in the request path via <c>IRequestContextProvider</c> because a
/// background scope has no <c>HttpContext</c>. Anonymised before it reaches this record (ADR 0024 D7),
/// so the queue holds no raw address.
/// </param>
/// <param name="UserAgent">The truncated User-Agent, captured for the same reason.</param>
public sealed record PasswordResetDispatch(string Email, string? IpAddress, string? UserAgent);

/// <summary>
/// #1171 — hands a forgot-password request to an out-of-band consumer so that NO existence-dependent
/// work happens on the request path.
/// <para>
/// <b>This exists to close a timing oracle, and the reason it is a queue rather than a faster send is
/// worth stating.</b> <c>/forgot-password</c> answers a uniform 202 for known and unknown addresses,
/// but the inline version paid an outbound HTTPS call to the mail provider only when the account
/// existed — a large, single-sample-classifiable difference. The per-address cooldown does not cap
/// that: enumeration needs exactly ONE measurement per candidate, and the cooldown only limits
/// REPEATED sampling of one address. What remained was the per-IP rate limit, which parallelises
/// trivially (security-auditor 2026-08-10).
/// </para>
/// <para>
/// With the lookup, the mint and the send all moved behind this port, the request path is capability
/// check → cooldown → <see cref="TryEnqueue"/>, none of which reads the account. The differential is
/// gone rather than reduced.
/// </para>
/// <para>
/// <b>The implementation is bounded and drops rather than blocks</b> — see its own docs. A blocking
/// enqueue would put a load-dependent delay back on an unauthenticated endpoint, which is the same
/// class of defect one step sideways.
/// </para>
/// </summary>
public interface IPasswordResetDispatcher
{
    /// <summary>
    /// Accepts a request for out-of-band dispatch. <b>Synchronous and non-blocking by contract</b> — an
    /// awaiting or blocking enqueue would reintroduce the latency channel this port exists to remove,
    /// so an implementation must return at once whether the queue is empty, full, or shutting down.
    /// <para>
    /// <b>The return does NOT mean "will be sent".</b> It means the implementation accepted the write.
    /// A saturated queue may accept and then discard — the implementation's own docs say which — and a
    /// discard is a server-health signal reported through logs, never through this value and never to
    /// the client. Callers must answer the uniform 202 regardless: the response may not vary with
    /// server load any more than it may vary with account existence.
    /// </para>
    /// </summary>
    bool TryEnqueue(PasswordResetDispatch dispatch);
}
