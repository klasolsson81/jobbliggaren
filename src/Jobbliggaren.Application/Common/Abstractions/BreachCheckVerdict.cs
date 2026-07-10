namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Outcome of a breached-password lookup (#616). Tri-state on purpose: the transport layer
/// classifies <em>what happened</em> (including <see cref="Unavailable"/> when the corpus cannot
/// be reached), while the fail-open POLICY — treating <see cref="Unavailable"/> as pass — lives
/// in the single consumer (<c>PwnedPasswordValidator</c>), not in the client. A bool would bury
/// that policy inside transport code and make it untestable as a policy pin (CTO-bind FORK 1/2).
/// </summary>
public enum BreachCheckVerdict
{
    /// <summary>The password was not found in the breach corpus.</summary>
    NotBreached,

    /// <summary>The password appears in the breach corpus (count ≥ 1) and must be rejected.</summary>
    Breached,

    /// <summary>
    /// The corpus could not be consulted (timeout, HTTP failure, DNS, open circuit). The caller
    /// decides the policy; per CTO-bind #616 the set-password flows fail open on this verdict.
    /// </summary>
    Unavailable,
}
