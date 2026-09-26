using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisOperatorContractTests(RedisBoundaryFixture fixture) : IClassFixture<RedisBoundaryFixture>
{
    [Theory]
    [InlineData("persistent", "api-persistent")]
    [InlineData("volatile", "api-volatile")]
    public async Task Operator_InspectsPersistenceAndPolicy_AndRevokesSyntheticIdentity(string kind, string application)
    {
        var store = kind == "persistent" ? fixture.Persistent : fixture.Volatile;
        var password = fixture.Password("operator-" + kind);
        // Test-only stdin file, never argv/environment. Real deployment keeps this credential on the host.
        await store.CopyAsync(System.Text.Encoding.UTF8.GetBytes(password), "/test/operator-password", ct: TestContext.Current.CancellationToken);
        async Task<DotNet.Testcontainers.Containers.ExecResult> Cli(params string[] args) =>
            await store.ExecAsync(["sh", "-c", "exec redis-cli -e --user operator-" + kind + " --askpass \"$@\" < /test/operator-password", "sh", .. args], TestContext.Current.CancellationToken);

        (await Cli("INFO", "persistence")).Stdout.ShouldContain("aof_enabled:" + (kind == "persistent" ? "1" : "0"));
        (await Cli("CONFIG", "GET", "appendonly")).Stdout.ShouldContain(kind == "persistent" ? "yes" : "no");
        if (kind == "volatile")
            (await Cli("CONFIG", "GET", "save")).Stdout.Trim().ShouldBe("save");
        (await Cli("ACL", "LIST")).Stdout.ShouldContain("user " + application);
        (await Cli("ACL", "DRYRUN", application, "PING")).Stdout.Trim().ShouldBe("OK");
        (await Cli("ACL", "DRYRUN", application, "CONFIG", "GET", "appendonly")).Stdout.ShouldContain("no permissions");
        (await Cli("GET", "jobbliggaren:session:probe")).ExitCode.ShouldNotBe(0);
        (await Cli("ACL", "SETUSER", "rotation-probe", "reset", "on", "#" + RedisBoundaryFixture.Hash(RedisBoundaryFixture.NewPassword()), "+ping")).Stdout.Trim().ShouldBe("OK");
        (await Cli("ACL", "DELUSER", "rotation-probe")).Stdout.Trim().ShouldBe("1");
        (await Cli("ACL", "LIST")).Stdout.ShouldNotContain("user rotation-probe");
        (await Cli("SAVE")).ExitCode.ShouldNotBe(0);
    }
}
