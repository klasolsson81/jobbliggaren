using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The connection to the NON-PERSISTED Redis instance (<c>redis-volatile</c>, ADR 0142 D1): the home of the
/// auth keys whose TTL has to be their whole lifetime — the login challenge's records and address index
/// (15 minutes) and the rate budgets (at most 24 hours). The closed-registration mail states those lifetimes
/// to the recipient, and on the durable instance an expired key stays in the AOF until the next rewrite.
///
/// <para>
/// <b>A type, not a second <see cref="IConnectionMultiplexer"/> registration.</b> An unkeyed second
/// registration is last-wins and would silently move every session onto an instance that forgets them at
/// each restart; a keyed one resolves the durable default the day someone forgets the key. A store that
/// takes this type cannot be handed the wrong instance, and one that does not take it cannot reach this one.
/// The multiplexer is therefore private and is never registered.
/// </para>
///
/// <para>
/// <b><see cref="ExecuteAsync{T}"/> is the only route to the database</b>, so a consumer cannot forget the
/// fault translation: a degraded instance surfaces as <see cref="VolatileRedisUnavailableException"/> and
/// no StackExchange.Redis type reaches the Api pipeline (CLAUDE.md §2.1). It wraps the WHOLE operation,
/// because a store's follow-up command belongs to the same unit as its transaction.
/// </para>
///
/// <para>
/// <b><c>AbortOnConnectFail = false</c>.</b> With the default, a down instance makes the constructor throw
/// from the singleton factory at first resolve, outside any guard, and the caller gets a 500. With it off,
/// the multiplexer comes up in reconnect mode and each command fails INSIDE <see cref="ExecuteAsync{T}"/>,
/// as the uniform 503. A lazy connect was rejected: <see cref="Lazy{T}"/> caches a thrown exception for the
/// life of the process, so one failed first attempt would be a permanent outage.
/// </para>
/// </summary>
internal sealed class VolatileRedisConnection : IDisposable
{
    private readonly ConnectionMultiplexer _multiplexer;

    internal VolatileRedisConnection(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        _multiplexer = ConnectionMultiplexer.Connect(options);
    }

    /// <summary>False while no endpoint is connected. Reads local state; never touches the network.</summary>
    internal bool IsConnected => _multiplexer.IsConnected;

    internal async Task<T> ExecuteAsync<T>(Func<IDatabase, Task<T>> operation)
    {
        try
        {
            return await operation(_multiplexer.GetDatabase());
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            throw new VolatileRedisUnavailableException(ex.GetType().Name);
        }
    }

    public void Dispose() => _multiplexer.Dispose();
}
