using DotNet.Testcontainers.Containers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1735 — what the deploy stack's <c>redis-volatile</c> DOES with a key once its TTL has passed, measured
/// on a container built from <c>deploy/docker-compose.yml</c>'s own fields
/// (<see cref="VolatileRedisContainer.FromDeployCompose"/>).
///
/// <para>
/// The sentence this makes true is the closed-registration mail's
/// (<c>EmailTemplates.LoginChallenge.cs</c>, "Därefter finns den inte kvar hos oss") and the processing
/// register's retention row for the login challenge. <c>EmailTemplatesLoginChallengeTests</c> pins the copy
/// against the policy's numbers; this class pins it against persistence.
/// </para>
///
/// <para>
/// Every verdict reads the FILESYSTEM, never <c>EXISTS</c>. On an instance with an append-only file,
/// <c>EXISTS</c> answers 0 after the TTL while the payload is still in the file, so a probe that asked Redis
/// would pass the durable instance too.
/// </para>
/// </summary>
public sealed class VolatileRedisPersistenceProbeTests : IAsyncLifetime
{
    private static readonly TimeSpan MarkerLifetime = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PastTheLifetime = TimeSpan.FromMilliseconds(3000);

    private readonly RedisContainer _redis = VolatileRedisContainer.FromDeployCompose();

    public async ValueTask InitializeAsync() => await _redis.StartAsync();

    public async ValueTask DisposeAsync() => await VolatileRedisContainer.DisposeAsync(_redis);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // redis-cli inside the container, not the application's client: StackExchange.Redis refuses CONFIG
    // without allowAdmin, and the fixture operator has explicit administrative authority; application ACL refusal is tested separately.
    private Task<ExecResult> CliAsync(params string[] arguments) =>
        _redis.ExecAsync(["sh", "-c", "exec redis-cli -e --user fixture-admin --askpass \"$@\" < /test/admin-password", "sh", .. arguments], Ct);

    private Task<ExecResult> ShellAsync(string script) => _redis.ExecAsync(["sh", "-c", script], Ct);

    private async Task<string> WriteAMarkerAndOutliveItAsync()
    {
        var marker = $"probe-{Guid.NewGuid():N}";
        var written = await CliAsync(
            "SET", $"probe:{marker}", marker, "PX",
            MarkerLifetime.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        written.Stdout.Trim().ShouldBe("OK");

        await Task.Delay(PastTheLifetime, Ct);
        (await CliAsync("EXISTS", $"probe:{marker}")).Stdout.Trim().ShouldBe("0");
        return marker;
    }

    [Fact]
    public async Task At_rest_an_expired_key_is_in_no_file_and_the_data_directory_holds_none()
    {
        var marker = await WriteAMarkerAndOutliveItAsync();

        var hits = await ShellAsync($"grep -ra '{marker}' /data | wc -l");
        hits.Stdout.Trim().ShouldBe("0");

        var listing = await ShellAsync("ls -A /data");
        listing.ExitCode.ShouldBe(0);
        listing.Stdout.Trim().ShouldBeEmpty("no dump.rdb and no appendonlydir: neither mechanism wrote");
    }

    [Fact]
    public async Task With_the_append_only_file_switched_on_by_the_fixture_operator_the_payload_reaches_only_the_tmpfs()
    {
        (await CliAsync("CONFIG", "SET", "appendonly", "yes")).Stdout.Trim().ShouldBe("OK");
        var marker = await WriteAMarkerAndOutliveItAsync();

        // The control: the attack DID happen. The flags are runtime-mutable and the payload outlives its
        // TTL in the file, which is the named residual of this design and the reason the MOUNT carries the
        // guarantee rather than the flags.
        var inTheFile = await ShellAsync($"grep -ral '{marker}' /data | wc -l");
        int.Parse(inTheFile.Stdout.Trim(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);

        // ...and the file it reached is RAM: /data is a tmpfs, so nothing here survives the container.
        var mount = await ShellAsync("grep ' /data ' /proc/mounts");
        mount.Stdout.ShouldStartWith("tmpfs /data tmpfs ");

        // Nowhere else is writable, so there is no second place for it to be.
        var elsewhere = await ShellAsync("touch /evidence");
        elsewhere.ExitCode.ShouldNotBe(0);
        elsewhere.Stderr.ShouldContain("Read-only file system");
    }

    [Fact]
    public async Task The_data_directory_cannot_be_moved_off_the_tmpfs()
    {
        // /data is Redis's only write path because `dir` is a protected config. Were it settable, a peer
        // could point the append-only file at a path outside the tmpfs.
        var moved = await CliAsync("CONFIG", "SET", "dir", "/tmp");

        (moved.Stdout + moved.Stderr).ShouldContain("protected config");
        (await CliAsync("CONFIG", "GET", "dir")).Stdout.ShouldContain("/data");
    }
}
