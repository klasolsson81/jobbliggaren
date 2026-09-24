using System.Globalization;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Containers;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisAccountMaintenanceTests
{
    [Fact]
    public void Runbook_DefaultTombstoneExample_MatchesSourceDefault()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Jobbliggaren.sln")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var runbook = File.ReadAllText(Path.Combine(root.FullName, "docs/runbooks/account-deletion.md"));
        var example = Regex.Match(runbook, @"source default example is `(\d+)` seconds");
        example.Success.ShouldBeTrue();
        double.Parse(example.Groups[1].Value, CultureInfo.InvariantCulture)
            .ShouldBe(new SessionStoreOptions().DeletionTombstoneTtl.TotalSeconds);
    }

    [Theory]
    [InlineData("redis:8.6-alpine")]
    [InlineData("redis:8.10-alpine")]
    public async Task Operator_AccountMaintenanceSelectors_AreStoreSpecificAndSurviveRestart(string image)
    {
        await using var fixture = new RedisBoundaryFixture(image);
        await fixture.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var (store, kind) in new[] { (fixture.Persistent, "persistent"), (fixture.Volatile, "volatile") })
            await store.CopyAsync(System.Text.Encoding.UTF8.GetBytes(fixture.Password("operator-" + kind)),
                "/test/operator-password", ct: cancellationToken);

        async Task<DotNet.Testcontainers.Containers.ExecResult> Cli(IContainer store, string kind, params string[] args) =>
            await store.ExecAsync(["sh", "-c", "exec redis-cli -e --raw --user operator-" + kind + " --askpass \"$@\" < /test/operator-password", "sh", .. args], cancellationToken);

        var account = Guid.NewGuid().ToString("D");
        var marker = $"jobbliggaren:user:{account}:deleted";
        var index = $"jobbliggaren:user:{account}:sessions";
        var session = "jobbliggaren:session:" + new string('A', 43);
        var admin = fixture.PersistentAdmin.GetDatabase();
        // The fixture administrator creates synthetic ACL probes, not an application session.
        await admin.SetAddAsync(index, "session:" + new string('A', 43));
        await admin.HashSetAsync(session, "data", "acl-probe");
        (await Cli(fixture.Persistent, "persistent", "SET", marker, "1", "EX", "2592000")).Stdout.Trim().ShouldBe("OK");
        (await Cli(fixture.Persistent, "persistent", "EXISTS", marker)).Stdout.Trim().ShouldBe("1");
        (await Cli(fixture.Persistent, "persistent", "DEL", marker)).Stdout.Trim().ShouldBe("1");
        (await Cli(fixture.Persistent, "persistent", "DEL", index)).Stdout.Trim().ShouldBe("1");
        (await Cli(fixture.Persistent, "persistent", "DEL", session)).Stdout.Trim().ShouldBe("1");

        string[][] forbidden =
        [
            ["GET", session], ["HGET", session, "data"], ["SMEMBERS", index],
            ["SET", session, "payload"], ["SET", index, "payload"],
            ["DEL", "jobbliggaren:landing:stats:v1"], ["DEL", "jobbliggaren:company:probe"],
            ["DEL", "jobbliggaren:auth/challenge/v1/probe"], ["SCAN", "0"], ["KEYS", "*"],
            ["DEL", marker, "jobbliggaren:landing:stats:v1"],
        ];
        foreach (var command in forbidden)
        {
            var result = await Cli(fixture.Persistent, "persistent", command);
            result.ExitCode.ShouldNotBe(0);
            result.Stderr.ShouldContain("NOPERM");
        }
        foreach (var command in new string[][] { ["SET", marker, "1", "EX", "10"], ["EXISTS", marker], ["DEL", marker], ["DEL", index], ["DEL", session] })
        {
            var result = await Cli(fixture.Volatile, "volatile", command);
            result.ExitCode.ShouldNotBe(0);
            result.Stderr.ShouldContain("NOPERM");
        }
        await fixture.Persistent.StopAsync(cancellationToken);
        await fixture.Persistent.StartAsync(cancellationToken);
        (await Cli(fixture.Persistent, "persistent", "SET", marker, "1", "EX", "2592000")).Stdout.Trim().ShouldBe("OK");
        (await Cli(fixture.Persistent, "persistent", "EXISTS", marker)).Stdout.Trim().ShouldBe("1");
        (await Cli(fixture.Persistent, "persistent", "DEL", "jobbliggaren:landing:stats:v1")).ExitCode.ShouldNotBe(0);
    }
}
