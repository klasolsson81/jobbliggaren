using Jobbliggaren.Infrastructure.Configuration;

namespace Jobbliggaren.Infrastructure.Auth;

internal sealed class ApiRedisStartupValidator(PersistentRedisReadinessProbe persistent, VolatileRedisConnection volatileRedis)
{
    internal async Task ValidateAsync(CancellationToken cancellationToken)
    {
        var persistentReady = persistent.CheckAsync(cancellationToken);
        bool volatileReady;
        try
        {
            await volatileRedis.ExecuteAsync(db => db.PingAsync())
                .WaitAsync(PersistentRedisReadinessProbe.Timeout, cancellationToken);
            volatileReady = true;
        }
        catch (VolatileRedisUnavailableException) { volatileReady = false; }
        catch (TimeoutException) { volatileReady = false; }
        catch (OperationCanceledException) { volatileReady = false; }

        if (!await persistentReady || !volatileReady)
            throw new InvalidOperationException("Redis startup verification failed.");
    }
}
