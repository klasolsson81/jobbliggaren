using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One Redis per test CLASS, flushed before every test, for the store adapters' contract suites.
/// Replaces a container per test method — measured 2026-09-24 on the runner (#1785): 136 Redis
/// lifecycles at 1.2 s each. A test that must STOP Redis takes its own container inline instead; a
/// stopped shared instance would fail every test after it.
/// </summary>
internal static class SharedRedis
{
    /// <summary>Empties every keyspace, so a test starts from the state a fresh container had.</summary>
    internal static async Task FlushAsync(ConnectionMultiplexer admin)
    {
        foreach (var endpoint in admin.GetEndPoints())
        {
            await admin.GetServer(endpoint).FlushAllDatabasesAsync();
        }
    }

    internal static async Task<ConnectionMultiplexer> ConnectAdminAsync(string connectionString) =>
        (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString + ",allowAdmin=true");
}

/// <summary>
/// The deploy stack's own <c>redis-volatile</c> (ACL, read-only rootfs, tmpfs, memory limit — see
/// <see cref="VolatileRedisContainer"/>), shared by one test class. <see cref="ConnectionString"/>
/// authenticates as the fixture's operator user.
/// </summary>
public sealed class SharedVolatileRedisFixture : IAsyncLifetime
{
    private ConnectionMultiplexer? _admin;

    public RedisContainer Container { get; } = VolatileRedisContainer.FromDeployCompose();

    public string ConnectionString => VolatileRedisContainer.OperatorConnectionString(Container);

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async Task FlushAsync()
    {
        _admin ??= await SharedRedis.ConnectAdminAsync(ConnectionString);
        await SharedRedis.FlushAsync(_admin);
    }

    public async ValueTask DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.CloseAsync();
            _admin.Dispose();
        }

        await VolatileRedisContainer.DisposeAsync(Container);
    }
}

/// <summary>A plain <c>redis:8-alpine</c>, shared by one test class.</summary>
public sealed class SharedPlainRedisFixture : IAsyncLifetime
{
    private ConnectionMultiplexer? _admin;

    public RedisContainer Container { get; } = new RedisBuilder("redis:8-alpine").Build();

    public string ConnectionString => Container.GetConnectionString();

    public async ValueTask InitializeAsync() => await Container.StartAsync();

    public async Task FlushAsync()
    {
        _admin ??= await SharedRedis.ConnectAdminAsync(ConnectionString);
        await SharedRedis.FlushAsync(_admin);
    }

    public async ValueTask DisposeAsync()
    {
        if (_admin is not null)
        {
            await _admin.CloseAsync();
            _admin.Dispose();
        }

        await Container.DisposeAsync();
    }
}
