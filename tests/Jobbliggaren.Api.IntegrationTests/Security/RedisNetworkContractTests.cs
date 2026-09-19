using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Security;

// Synthetic listeners prove the proposed topology; production Compose adoption is a separate gate.
public sealed class RedisNetworkContractTests : IAsyncLifetime
{
    private readonly BoundaryContract _contract = JsonSerializer.Deserialize<BoundaryContract>(
        RedisBoundaryFixture.Resource("service-boundaries.json"), JsonSerializerOptions.Web)!;
    private readonly List<INetwork> _networks = [];
    private readonly Dictionary<string, IContainer> _services = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _addresses = new(StringComparer.Ordinal);
    private readonly string _secrets = Path.Combine(Path.GetTempPath(), "jbl-redis-boundary-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_secrets);
        foreach (var secret in _contract.SecretFiles.Values.SelectMany(x => x).Distinct())
            await File.WriteAllTextAsync(Path.Combine(_secrets, secret), "synthetic-fixture-only", Ct);

        var membership = _contract.InternalPairs.SelectMany(x => x).Distinct()
            .ToDictionary(x => x, _ => new List<INetwork>(), StringComparer.Ordinal);
        foreach (var pair in _contract.InternalPairs)
        {
            pair.Length.ShouldBe(2);
            var network = new NetworkBuilder().WithDriver(NetworkDriver.Bridge)
                .WithCreateParameterModifier(p => p.Internal = true).Build();
            _networks.Add(network);
            foreach (var service in pair)
                membership[service].Add(network);
        }
        foreach (var owner in _contract.EgressOwners)
        {
            var network = new NetworkBuilder().WithDriver(NetworkDriver.Bridge).Build();
            _networks.Add(network);
            membership[owner].Add(network);
        }
        await Task.WhenAll(_networks.Select(x => x.CreateAsync(Ct)));

        foreach (var (name, networks) in membership)
        {
            var builder = new ContainerBuilder(RedisBoundaryFixture.Image)
                .WithNetworkAliases(name)
                .WithCommand("redis-server", "--save", "", "--appendonly", "no", "--protected-mode", "no")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"));
            foreach (var network in networks)
                builder = builder.WithNetwork(network);
            foreach (var secret in _contract.SecretFiles.GetValueOrDefault(name, []))
                builder = builder.WithBindMount(Path.Combine(_secrets, secret), "/run/redis-identity/" + secret, AccessMode.ReadOnly);
            _services.Add(name, builder.Build());
        }
        await Task.WhenAll(_services.Values.Select(x => x.StartAsync(Ct)));
        foreach (var (name, service) in _services)
        {
            var result = await service.ExecAsync(["hostname", "-i"], Ct);
            result.ExitCode.ShouldBe(0);
            var addresses = result.Stdout.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            addresses.ShouldNotBeEmpty();
            addresses.All(x => IPAddress.TryParse(x, out _)).ShouldBeTrue();
            _addresses.Add(name, addresses);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_services.Values.Select(x => x.DisposeAsync().AsTask()));
        await Task.WhenAll(_networks.Select(x => x.DisposeAsync().AsTask()));
        if (Directory.Exists(_secrets))
            Directory.Delete(_secrets, recursive: true);
    }

    [Fact]
    public async Task PairwiseBridges_IntendedPeers_ResolveAndConnectInBothDirections()
    {
        _contract.Version.ShouldBe(1);
        foreach (var pair in _contract.InternalPairs)
        {
            foreach (var (from, to) in new[] { (pair[0], pair[1]), (pair[1], pair[0]) })
            {
                (await PingAsync(from, to)).ShouldBeTrue($"{from} -> {to} must resolve and listen");
                var probes = await Task.WhenAll(_addresses[to].Select(address => PingAsync(from, address)));
                probes.Any(x => x).ShouldBeTrue($"{from} -> {to} must work by direct address");
            }
        }
    }

    [Fact]
    public async Task RedisPaths_UnrelatedServices_CannotReachDnsOrAnyDirectAddress()
    {
        foreach (var target in new[] { "redis-persistent", "redis-volatile" })
        {
            // Every target interface must really listen before a refusal counts as network evidence.
            foreach (var address in _addresses[target])
                (await PingAsync(target, address)).ShouldBeTrue();

            var permitted = target == "redis-persistent" ? new[] { "api", "worker" } : ["api"];
            var forbidden = _services.Keys.Where(x => x != target && !permitted.Contains(x)).ToArray();
            await Task.WhenAll(forbidden.Select(async from =>
            {
                (await PingAsync(from, target)).ShouldBeFalse($"{from} must not reach {target} by DNS");
                foreach (var address in _addresses[target])
                    (await PingAsync(from, address)).ShouldBeFalse($"{from} must not reach {target} by direct address");
            }));
        }
    }

    [Fact]
    public async Task SecretMounts_EachService_SeesOnlyItsReadOnlyFiles()
    {
        _contract.SecretFiles["api"].ShouldBe(["api-persistent", "api-volatile"]);
        _contract.SecretFiles["worker"].ShouldBe(["worker-persistent"]);
        _contract.SecretFiles["redis-persistent"].ShouldBe(["persistent-acl", "health-persistent"]);
        _contract.SecretFiles["redis-volatile"].ShouldBe(["volatile-acl", "health-volatile"]);
        _contract.SecretFiles.Keys.ShouldBe(["api", "worker", "redis-persistent", "redis-volatile"], ignoreOrder: true);
        var secrets = _contract.SecretFiles.Values.SelectMany(x => x).Distinct().ToArray();
        foreach (var (name, service) in _services)
        {
            var expected = _contract.SecretFiles.GetValueOrDefault(name, []);
            foreach (var secret in secrets)
            {
                var path = "/run/redis-identity/" + secret;
                var read = await service.ExecAsync(["test", "-r", path], Ct);
                (read.ExitCode == 0).ShouldBe(expected.Contains(secret), $"{name}: visibility of {secret}");
                if (expected.Contains(secret))
                {
                    var write = await service.ExecAsync(["sh", "-c", "printf changed > \"$1\"", "probe", path], Ct);
                    write.ExitCode.ShouldNotBe(0, $"{name}: {secret} must be a read-only mount");
                }
            }
        }
    }

    [Fact]
    public async Task UnrelatedBridges_EgressAndDataPeers_RemainSeparated()
    {
        _contract.EgressOwners.ShouldBe(["caddy", "api", "worker"]);
        var forbidden = new[] { ("caddy", "api"), ("caddy", "worker"), ("caddy", "postgres"),
            ("web", "worker"), ("web", "postgres"), ("web", "seq"), ("api", "worker"), ("worker", "api") };
        await Task.WhenAll(forbidden.Select(async pair =>
        {
            (await PingAsync(pair.Item2, "127.0.0.1")).ShouldBeTrue();
            (await PingAsync(pair.Item1, pair.Item2)).ShouldBeFalse($"{pair.Item1} -> {pair.Item2} by DNS");
            foreach (var address in _addresses[pair.Item2])
                (await PingAsync(pair.Item1, address)).ShouldBeFalse($"{pair.Item1} -> {pair.Item2} by direct address");
        }));
    }

    private async Task<bool> PingAsync(string from, string address)
    {
        var result = await _services[from].ExecAsync(["timeout", "2", "redis-cli", "-e", "-h", address, "PING"], Ct);
        return result.ExitCode == 0 && (result.Stdout + result.Stderr).TrimEnd('\r', '\n') == "PONG";
    }

    private sealed record BoundaryContract(int Version, string[][] InternalPairs,
        string[] EgressOwners, Dictionary<string, string[]> SecretFiles);
}
