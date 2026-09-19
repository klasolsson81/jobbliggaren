using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisBoundaryFixture : IAsyncLifetime
{
    internal const string Image = "redis:8.6-alpine";
    internal const string ApiPersistent = "api-persistent";
    internal const string WorkerPersistent = "worker-persistent";
    internal const string ApiVolatile = "api-volatile";
    internal const string Admin = "fixture-admin";
    private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);
    private readonly List<ConnectionMultiplexer> _connections = [];
    private readonly INetwork _persistentNetwork = new NetworkBuilder().Build();
    private readonly INetwork _volatileNetwork = new NetworkBuilder().Build();
    internal IContainer Persistent { get; }
    internal IContainer Volatile { get; }
    internal ConnectionMultiplexer Api { get; private set; } = null!;
    internal ConnectionMultiplexer Worker { get; private set; } = null!;
    internal ConnectionMultiplexer Challenge { get; private set; } = null!;

    // #1735 — the stores reach the volatile instance only through VolatileRedisConnection, which owns a
    // private multiplexer. Built from the same options as Challenge, so an adapter under test runs as the
    // same `api-volatile` ACL identity.
    internal VolatileRedisConnection ChallengeAdapter { get; private set; } = null!;
    internal ConnectionMultiplexer PersistentAdmin { get; private set; } = null!;
    internal ConnectionMultiplexer VolatileAdmin { get; private set; } = null!;

    public RedisBoundaryFixture()
    {
        foreach (var user in new[] { ApiPersistent, WorkerPersistent, ApiVolatile, Admin, "health-persistent", "health-volatile" })
            _passwords.Add(user, NewPassword());

        Persistent = BuildStore("persistent", persisted: true);
        Volatile = BuildStore("volatile", persisted: false);
    }

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_persistentNetwork.CreateAsync(), _volatileNetwork.CreateAsync());
        await Task.WhenAll(Persistent.StartAsync(), Volatile.StartAsync());
        PersistentAdmin = await ConnectAsync(Persistent, Admin);
        VolatileAdmin = await ConnectAsync(Volatile, Admin);
        Api = await ConnectAsync(Persistent, ApiPersistent);
        Worker = await ConnectAsync(Persistent, WorkerPersistent);
        Challenge = await ConnectAsync(Volatile, ApiVolatile);
        ChallengeAdapter = new VolatileRedisConnection(OptionsFor(Volatile, ApiVolatile).ToString());
    }

    public async ValueTask DisposeAsync()
    {
        ChallengeAdapter?.Dispose();
        foreach (var connection in _connections)
            connection.Dispose();
        await Volatile.DisposeAsync();
        await Persistent.DisposeAsync();
        await _volatileNetwork.DisposeAsync();
        await _persistentNetwork.DisposeAsync();
    }

    internal static string Resource(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RedisPolicy", name));

    internal string Password(string user) => _passwords[user];
    internal static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal static string Hash(string password) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    internal ConfigurationOptions OptionsFor(IContainer store, string user, string? password = null)
    {
        var options = new ConfigurationOptions
        {
            User = user,
            Password = password ?? _passwords[user],
            AbortOnConnectFail = true,
            ConnectRetry = 0,
            ConnectTimeout = 1500,
            AsyncTimeout = 3000,
            SyncTimeout = 3000,
            DefaultVersion = new Version(8, 6),
            Protocol = RedisProtocol.Resp2,
            ConfigurationChannel = "",
            TieBreaker = "",
            AllowAdmin = user == Admin,
            CommandMap = CommandMap.Create(
                ["CONFIG", "INFO", "CLUSTER", "SENTINEL", "SUBSCRIBE", "PSUBSCRIBE",
                 "UNSUBSCRIBE", "PUNSUBSCRIBE", "PUBLISH", "SELECT"], available: false),
        };
        options.EndPoints.Add(store.Hostname, store.GetMappedPublicPort(6379));
        return options;
    }

    internal async Task<ConnectionMultiplexer> ConnectAsync(IContainer store, string user, string? password = null)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(OptionsFor(store, user, password));
        _connections.Add(connection);
        return connection;
    }

    internal static RedisCache Cache(IConnectionMultiplexer connection) =>
        new(Options.Create(new RedisCacheOptions
        {
            InstanceName = "jobbliggaren:",
            ConnectionMultiplexerFactory = () => Task.FromResult(connection),
        }));

    internal string RenderPolicy(string kind)
    {
        var policy = Resource(kind + ".acl.template");
        foreach (var (user, password) in _passwords)
            policy = policy.Replace("{{" + user.ToUpperInvariant().Replace('-', '_') + "_SHA256}}", Hash(password), StringComparison.Ordinal);
        if (policy.Contains("{{", StringComparison.Ordinal))
            throw new InvalidOperationException("Unresolved ACL credential placeholder.");

        // The control connection exists only in this fixture, never in the deployment policy.
        policy += $"\nuser {Admin} reset on #{Hash(_passwords[Admin])} ~* &* +@all\n";
        return policy;
    }

    private IContainer BuildStore(string kind, bool persisted)
    {
        return new ContainerBuilder(Image)
            .WithNetwork(persisted ? _persistentNetwork : _volatileNetwork)
            .WithPortBinding(6379, true)
            .WithTmpfsMount("/data")
            .WithResourceMapping(Encoding.UTF8.GetBytes(RenderPolicy(kind)), "/etc/redis/users.acl")
            .WithResourceMapping(Encoding.UTF8.GetBytes(_passwords["health-" + kind]), "/test/health-password")
            .WithResourceMapping(Encoding.UTF8.GetBytes(NewPassword()), "/test/wrong-password")
            .WithResourceMapping(Encoding.UTF8.GetBytes(Resource("healthcheck.sh")), "/test/healthcheck.sh")
            .WithCommand("redis-server", "--aclfile", "/etc/redis/users.acl", "--save", "",
                "--appendonly", persisted ? "yes" : "no", "--maxmemory", "64mb", "--maxmemory-policy", "noeviction")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
    }
}
