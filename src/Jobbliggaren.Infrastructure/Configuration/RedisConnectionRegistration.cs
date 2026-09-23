using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Configuration;

public static class RedisConnectionRegistration
{
    internal const string ForceReconnectSwitch = "Microsoft.AspNetCore.Caching.StackExchangeRedis.UseForceReconnect";

    public static IServiceCollection AddApiRedisConnections(this IServiceCollection services, IConfiguration configuration)
    {
        AddPersistent(services, configuration, RedisClientIdentity.ApiPersistent);
        var options = RedisClientConfiguration.Create(
            configuration.GetConnectionString(DependencyInjection.VolatileRedisConnectionStringName), RedisClientIdentity.ApiVolatile);
        services.AddSingleton(_ => CreateVolatile(options));
        services.AddSingleton<ApiRedisStartupValidator>();
        return services;
    }

    public static IServiceCollection AddWorkerRedisConnection(this IServiceCollection services, IConfiguration configuration) =>
        AddPersistent(services, configuration, RedisClientIdentity.WorkerPersistent);

    private static IServiceCollection AddPersistent(IServiceCollection services, IConfiguration configuration, RedisClientIdentity identity)
    {
        if (AppContext.TryGetSwitch(ForceReconnectSwitch, out var forceReconnect) && forceReconnect)
            throw new InvalidOperationException("Redis cache force reconnect is incompatible with the shared application connection.");
        var options = RedisClientConfiguration.Create(configuration.GetConnectionString("Redis"), identity);
        services.AddSingleton<IConnectionMultiplexer>(_ => Connect(options));
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<IConnectionMultiplexer>((cache, connection) =>
        {
            cache.InstanceName = "jobbliggaren:";
            cache.ConnectionMultiplexerFactory = () => Task.FromResult(connection);
        });
        services.AddSingleton<PersistentRedisReadinessProbe>();
        return services;
    }

    private static ConnectionMultiplexer Connect(ConfigurationOptions options)
    {
        try { return ConnectionMultiplexer.Connect(options); }
        catch (RedisException) { throw new InvalidOperationException("Persistent Redis connection could not be established."); }
    }

    private static VolatileRedisConnection CreateVolatile(ConfigurationOptions options)
    {
        try { return new VolatileRedisConnection(options); }
        catch (RedisException) { throw new InvalidOperationException("Volatile Redis connection could not be established."); }
    }

    public static Task RequireApiRedisReadyAsync(this IServiceProvider services, CancellationToken cancellationToken = default) =>
        services.GetRequiredService<ApiRedisStartupValidator>().ValidateAsync(cancellationToken);

    public static async Task RequireWorkerRedisReadyAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!await services.GetRequiredService<PersistentRedisReadinessProbe>().CheckAsync(cancellationToken))
            throw new InvalidOperationException("Persistent Redis startup verification failed.");
    }
}
