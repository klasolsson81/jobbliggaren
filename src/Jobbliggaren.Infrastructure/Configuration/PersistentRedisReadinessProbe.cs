using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Configuration;

public sealed class PersistentRedisReadinessProbe(IConnectionMultiplexer connection)
{
    private readonly object _gate = new();
    private Task<bool>? _pending;
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        Task<bool> pending;
        lock (_gate)
        {
            if (_pending is { IsCompleted: false })
                return false;
            pending = _pending = PingAsync();
        }
        try { return await pending.WaitAsync(Timeout, cancellationToken); }
        catch (TimeoutException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task<bool> PingAsync()
    {
        try
        {
            await connection.GetDatabase().PingAsync();
            return true;
        }
        catch (RedisException) { return false; }
        catch (TimeoutException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }
}
