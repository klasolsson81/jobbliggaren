using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Configuration;

internal static class RedisClientConfiguration
{
    internal static ConfigurationOptions Create(string? connectionString, RedisClientIdentity identity)
    {
        var expectedUser = identity switch
        {
            RedisClientIdentity.ApiPersistent => "api-persistent",
            RedisClientIdentity.WorkerPersistent => "worker-persistent",
            RedisClientIdentity.ApiVolatile => "api-volatile",
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };

        if (string.IsNullOrWhiteSpace(connectionString))
            throw InvalidConfiguration(expectedUser);

        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            throw InvalidConfiguration(expectedUser);
        }

        if (!string.Equals(options.User, expectedUser, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(options.Password)
            || options.EndPoints.Count != 1
            || options.DefaultDatabase is not (null or 0)
            || !string.IsNullOrEmpty(options.ServiceName)
            || options.Proxy != Proxy.None)
        {
            throw InvalidConfiguration(expectedUser);
        }

        options.DefaultDatabase = 0;
        options.DefaultVersion = new Version(8, 6);
        options.Protocol = RedisProtocol.Resp2;
        options.ConfigurationChannel = "";
        options.TieBreaker = "";
        options.AllowAdmin = false;
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 0;
        options.ConnectTimeout = 2000;
        options.AsyncTimeout = 1500;
        options.SyncTimeout = 1500;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        options.IncludeDetailInExceptions = false;
        options.IncludePerformanceCountersInExceptions = false;
        options.CommandMap = CommandMap.Create(
            ["CONFIG", "INFO", "CLUSTER", "SENTINEL", "SUBSCRIBE", "PSUBSCRIBE",
             "UNSUBSCRIBE", "PUNSUBSCRIBE", "PUBLISH", "SELECT"], available: false);
        return options;
    }

    private static InvalidOperationException InvalidConfiguration(string expectedUser) =>
        new($"Redis configuration for '{expectedUser}' is invalid.");
}
