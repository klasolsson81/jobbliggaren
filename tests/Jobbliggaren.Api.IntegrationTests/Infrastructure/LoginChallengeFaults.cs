using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Puts the login challenge's two Redis stores out of reach for the duration of a scope, so the Api-level
/// 503 can be driven end to end without a fourth <c>WebApplicationFactory</c> (the EF ceiling documented on
/// <see cref="RecordingEmailSender.Incapable"/>). The fault thrown is the adapters' own contract: on a Redis
/// fault <c>RedisRateBudget</c> and <c>RedisLoginChallengeStore</c> throw
/// <see cref="VolatileRedisUnavailableException"/> with the Redis exception's type name, which
/// <c>RedisRateBudgetTests</c> and <c>RedisLoginChallengeStoreTests</c> measure against a stopped container.
/// </summary>
internal sealed class LoginChallengeFaults
{
    private volatile bool _unavailable;

    internal IDisposable Unavailable()
    {
        _unavailable = true;
        return new Scope(this);
    }

    internal void ThrowIfUnavailable()
    {
        if (_unavailable)
            throw new VolatileRedisUnavailableException("RedisConnectionException");
    }

    private sealed class Scope(LoginChallengeFaults owner) : IDisposable
    {
        public void Dispose() => owner._unavailable = false;
    }
}

internal sealed class FaultableRateBudget(IRateBudget inner, LoginChallengeFaults faults) : IRateBudget
{
    public Task<bool> TryConsumeAsync(RateBudgetScope scope, string subject, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.TryConsumeAsync(scope, subject, ct);
    }
}

internal sealed class FaultableLoginChallengeStore(ILoginChallengeStore inner, LoginChallengeFaults faults)
    : ILoginChallengeStore
{
    public Task<IssuedCredentials> PutAsync(NewLoginChallenge challenge, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.PutAsync(challenge, ct);
    }

    public Task<ChallengeVerdict> ConsumeCodeAsync(ChallengeId id, LoginCode presented, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.ConsumeCodeAsync(id, presented, ct);
    }

    public Task<LoginChallengeProof?> ConsumeLinkAsync(LoginLinkToken token, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.ConsumeLinkAsync(token, ct);
    }

    public Task<LoginCode> PutBoundAsync(NewBoundChallenge challenge, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.PutBoundAsync(challenge, ct);
    }

    public Task<ChallengeVerdict> ConsumeBoundCodeAsync(
        ChallengeId id, LoginCode presented, ChallengeBinding expected, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.ConsumeBoundCodeAsync(id, presented, expected, ct);
    }
}

internal sealed class FaultableGrantStore(IGrantStore inner, LoginChallengeFaults faults) : IGrantStore
{
    public Task<GrantToken> IssueAsync(GrantSubject subject, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.IssueAsync(subject, ct);
    }

    public Task<GrantSubject?> RedeemAsync(GrantToken token, GrantAssertion expected, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.RedeemAsync(token, expected, ct);
    }
}

internal sealed class FaultableOAuthStateStore(IOAuthStateStore inner, LoginChallengeFaults faults) : IOAuthStateStore
{
    private int _writes;

    /// <summary>#1744 — how many flows this host handed to the real store, so a refused start can be pinned as unwritten.</summary>
    internal int Writes => Volatile.Read(ref _writes);

    public Task<OAuthState> PutAsync(OAuthFlow flow, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        Interlocked.Increment(ref _writes);
        return inner.PutAsync(flow, ct);
    }

    public Task<OAuthFlow?> TakeAsync(OAuthState state, ExternalProviderKey expected, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.TakeAsync(state, expected, ct);
    }
}
