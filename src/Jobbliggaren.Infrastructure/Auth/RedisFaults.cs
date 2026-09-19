using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The login challenge's Redis stores are unreachable. <c>Program.cs</c> renders it as the uniform 503, the
/// same way it renders an unreachable session store (ADR 0142 D2). It is thrown from inside the Mediator
/// pipeline, so it carries the Redis failure's type name and never the Redis exception itself.
/// </summary>
public sealed class LoginChallengeStoreUnavailableException(string innerType)
    : StoreUnavailableException("Login challenge store (Redis) unavailable.", innerType);

/// <summary>
/// Translates a degraded Redis into <see cref="LoginChallengeStoreUnavailableException"/> for the login
/// challenge's stores, with the same exception filter <c>SessionStoreResilienceDecorator</c> uses, so no
/// StackExchange.Redis type reaches the Api pipeline (CLAUDE.md §2.1).
/// </summary>
internal static class RedisFaults
{
    internal static async Task<T> GuardAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            throw new LoginChallengeStoreUnavailableException(ex.GetType().Name);
        }
    }
}
