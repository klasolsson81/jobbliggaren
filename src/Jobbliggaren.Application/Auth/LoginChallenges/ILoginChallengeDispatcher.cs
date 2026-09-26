using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// One queued login-challenge request. It carries the SUBMITTED address and what the request path decided
/// without reading the account; everything that depends on the account happens on the consumer side.
/// </summary>
/// <param name="Email">The address as submitted. Never logged.</param>
/// <param name="CodeBudget">Whether the request's code budget admitted a code.</param>
/// <param name="IpAddress">The ANONYMISED client IP, captured on the request path (ADR 0024 D7).</param>
/// <param name="UserAgent">The truncated User-Agent, captured on the request path.</param>
public sealed record LoginChallengeDispatch(
    ChallengeId ChallengeId,
    string Email,
    CodeBudgetState CodeBudget,
    string? IpAddress,
    string? UserAgent);

/// <summary>
/// Hands a login challenge to the out-of-band consumer, so no existence-dependent work runs on the request
/// path (ADR 0142 D2). Its own port and its own bounded queue.
/// <para>
/// <b>Returns nothing, and that is the contract.</b> No caller can branch on an enqueue result, because
/// there is none: the request path answers its uniform 202 whether the queue took the item, dropped it on a
/// full queue, or is shutting down. A drop is an operator signal, logged by the implementation.
/// </para>
/// </summary>
public interface ILoginChallengeDispatcher
{
    /// <summary>Synchronous and non-blocking: returns at once whether the queue is empty or full.</summary>
    void Enqueue(LoginChallengeDispatch dispatch);
}

/// <summary>
/// Bounds the login-challenge dispatch queue. Bound under <c>Auth:LoginChallengeDispatch</c> with
/// <c>ValidateDataAnnotations().ValidateOnStart()</c>. A valid code default, so a fresh dev boot needs no
/// key for it (CLAUDE.md §11 is not triggered).
/// </summary>
public sealed class LoginChallengeDispatchOptions
{
    public const string SectionName = "Auth:LoginChallengeDispatch";

    /// <summary>
    /// Queued requests held before further ones are dropped. Bounded because the producer is an
    /// unauthenticated endpoint; the default is derived from one consumer and a provider round trip per
    /// item.
    /// </summary>
    [Range(1, 100_000)]
    public int Capacity { get; set; } = 1000;
}
