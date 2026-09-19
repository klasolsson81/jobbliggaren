using System.Text.Json;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1735 — pins the FORM of the non-persisted Redis both stacks declare for the login challenge's stores
/// (ADR 0142 D1): nothing the instance holds may reach a disk.
///
/// <para>
/// Three layers, and each test below names the one it holds. <c>--save ""</c> and <c>--appendonly no</c>
/// write nothing by themselves, but both are runtime-mutable — any client on the network can
/// <c>CONFIG SET appendonly yes</c> (measured on Redis 8.6.5 and 8.10.1, 2026-09-19) — so the MOUNT is what makes a
/// disk unreachable: <c>/data</c> is Redis's only write path, and it is a sized tmpfs under a read-only
/// root. The deploy stack adds a cgroup limit the flood cannot reach before Redis's own refusal.
/// </para>
///
/// <para>
/// Every lookup of a service's key is scoped to that SERVICE BLOCK, never to the file — the trap
/// <see cref="DeployComposeDataProtectionTests"/> records: a key hoisted into an <c>x-*</c> anchor still
/// occurs exactly once while a second service silently inherits it.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeVolatileRedisTests
{
    private const string DeployFile = "deploy/docker-compose.yml";
    private const string DevFile = "docker-compose.yml";
    private const string DeployService = "redis-volatile";
    private const string DevService = "redis-volatile-dev";
    private const string TmpfsPrefix = "/data:size=";

    /// <summary>Derived from the constant the Api reads, never restated (ASP.NET Core maps <c>__</c> to <c>:</c>).</summary>
    private static readonly string KeyVariable =
        "ConnectionStrings__" + DependencyInjection.VolatileRedisConnectionStringName;

    private static readonly ComposeFile Deploy = new(DeployFile);
    private static readonly ComposeFile Dev = new(DevFile);

    /// <summary>The volatile service in each stack.</summary>
    public static TheoryData<string, string> BothStacks => new()
    {
        { DeployFile, DeployService },
        { DevFile, DevService },
    };

    /// <summary>The volatile service in each stack, with the durable sibling it must differ from.</summary>
    public static TheoryData<string, string, string> BothStacksWithDurableSibling => new()
    {
        { DeployFile, DeployService, "redis" },
        { DevFile, DevService, "redis-dev" },
    };

    /// <summary>The volatile service in each stack, with every key its block declares.</summary>
    public static TheoryData<string, string, string[]> BothStacksWithTheirKeys => new()
    {
        {
            DeployFile, DeployService,
            [
                "image", "container_name", "command", "read_only", "tmpfs", "mem_limit", "memswap_limit",
                "healthcheck", "security_opt", "logging", "restart", "networks",
            ]
        },
        {
            DevFile, DevService,
            ["image", "container_name", "command", "read_only", "tmpfs", "ports", "healthcheck", "restart"]
        },
    };

    private static IReadOnlyList<string> Command(IReadOnlyList<string> block) =>
        ComposeFile.ListUnder(block, "command");

    private static string ArgumentAfter(IReadOnlyList<string> command, string flag) =>
        command.SkipWhile(a => a != flag).Skip(1).FirstOrDefault()
        ?? throw new InvalidOperationException($"`{flag}` is missing from the command, or is its last item.");

    [Theory]
    [MemberData(nameof(BothStacks))]
    public void Instance_BothPersistenceMechanisms_AreSwitchedOff(string file, string service)
    {
        var command = Command(new ComposeFile(file).ServiceBlock(service));

        // `save ""` is an EMPTY argument and has to be its own list item: folded into `--save`'s item, or
        // dropped, Redis keeps its built-in save points and writes an RDB with AOF off.
        ArgumentAfter(command, "--save").ShouldBe("\"\"");
        ArgumentAfter(command, "--appendonly").ShouldBe("\"no\"");
    }

    [Theory]
    [MemberData(nameof(BothStacksWithDurableSibling))]
    public void Instance_DeclaresNoVolume_WhereItsDurableSiblingDoes(string file, string service, string durable)
    {
        var compose = new ComposeFile(file);

        ComposeFile.ListUnder(compose.ServiceBlock(service), "volumes").ShouldBeEmpty();
        ComposeFile.Setting(compose.ServiceBlock(service), "volumes").ShouldBeNull();

        // The control the absence is measured against: the same reader, on the service that DOES persist,
        // finds its volume. A reader that found nothing anywhere would pass the two lines above.
        ComposeFile.ListUnder(compose.ServiceBlock(durable), "volumes")
            .ShouldContain(v => v.EndsWith(":/data", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BothStacks))]
    public void Instance_DataDirectory_IsASizedTmpfsUnderAReadOnlyRoot(string file, string service)
    {
        var block = new ComposeFile(file).ServiceBlock(service);

        ComposeFile.Setting(block, "read_only").ShouldBe("true");

        // An explicit size: without one, a switched-on AOF grows until the cgroup kills the container.
        var mount = ComposeFile.ListUnder(block, "tmpfs").ShouldHaveSingleItem();
        mount.ShouldStartWith(TmpfsPrefix);
        ComposeFile.Mebibytes(mount[TmpfsPrefix.Length..]).ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(BothStacks))]
    public void Instance_RefusesWritesWhenFull_RatherThanEvicting(string file, string service)
    {
        // An evicting policy lets a flood evict a target's budget key: a budget reset the attacker chooses.
        var command = Command(new ComposeFile(file).ServiceBlock(service));

        ArgumentAfter(command, "--maxmemory-policy").ShouldBe("noeviction");
        ComposeFile.Mebibytes(ArgumentAfter(command, "--maxmemory")).ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(BothStacksWithDurableSibling))]
    public void Instance_Image_IsItsDurableSiblingsTag(string file, string service, string durable)
    {
        // In the deploy stack this is load-bearing: jobbliggaren-reconcile.sh allow-lists upstream images by
        // image:tag, so one entry covers both only while the two lines are identical, and a drift makes the
        // reconcile refuse the whole apply, hourly.
        var compose = new ComposeFile(file);

        ComposeFile.Setting(compose.ServiceBlock(service), "image").ShouldNotBeNull()
            .ShouldBe(ComposeFile.Setting(compose.ServiceBlock(durable), "image"));
    }

    [Theory]
    [MemberData(nameof(BothStacksWithTheirKeys))]
    public void Instance_DeclaredKeys_AreExactlyThePinnedSet(string file, string service, string[] expected)
    {
        // Every other lookup in this class asks for one key at a time, so a key none of them names passes:
        // `volumes_from: [redis]` mounts the durable sibling's volume, and compose ignores whatever is
        // nested under an `x-*` key.
        ComposeFile.Keys(new ComposeFile(file).ServiceBlock(service)).ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void Redis_TheDurableInstance_StillRunsAnAppendOnlyFile()
    {
        // The second control: the flag reader sees `"yes"` where it is written, so `"no"` above is a reading.
        ArgumentAfter(Command(Deploy.ServiceBlock("redis")), "--appendonly").ShouldBe("\"yes\"");
    }

    [Fact]
    public void RedisVolatile_CgroupLimit_ClearsTwiceMaxmemoryPlusTheTmpfs_AndAllowsNoSwap()
    {
        // The RELATION, not three numbers: a limit the flood reaches before Redis's own refusal is a SIGKILL,
        // a restart, and every budget counter reset on the attacker's schedule.
        // Deploy stack only — the limits answer a hostile peer on a shared host.
        var block = Deploy.ServiceBlock(DeployService);
        var maxmemory = ComposeFile.Mebibytes(ArgumentAfter(Command(block), "--maxmemory"));
        var tmpfs = ComposeFile.Mebibytes(
            ComposeFile.ListUnder(block, "tmpfs").ShouldHaveSingleItem()[TmpfsPrefix.Length..]);
        var memLimit = ComposeFile.Setting(block, "mem_limit").ShouldNotBeNull();

        ComposeFile.Mebibytes(memLimit).ShouldBeGreaterThanOrEqualTo(2 * maxmemory + tmpfs);
        ComposeFile.Setting(block, "memswap_limit").ShouldBe(memLimit,
            customMessage: "memswap_limit == mem_limit is swap.max = 0: without it the dataset can reach a disk "
                           + "through swap, which is the persistence this service exists to exclude.");
    }

    [Fact]
    public void Api_IsPointedAtTheVolatileInstance_ByLiteralValue_AndWaitsForIt()
    {
        // The VALUE, not only the key's presence: a key that resolved to the durable instance would defeat
        // this one silently, and string equality at boot cannot catch it (`redis:6379,abortConnect=false`).
        var api = Deploy.ServiceBlock("api").ToList();

        ComposeFile.Setting(api, KeyVariable).ShouldBe($"\"{DeployService}:6379\"");

        var dependency = api.FindIndex(l => l.Trim() == $"{DeployService}:");
        dependency.ShouldBeGreaterThan(-1, "the api service does not depend on the volatile instance");
        api[dependency + 1].Trim().ShouldBe("condition: service_healthy");
    }

    [Fact]
    public void Worker_IsNotGivenTheKey_AndNoTopLevelAnchorCarriesIt()
    {
        // The worker composes neither store. Hoisted into x-app-connections the key would still occur once
        // in the file while BOTH services merged it.
        var worker = Deploy.ServiceBlock("worker");

        ComposeFile.Setting(worker, KeyVariable).ShouldBeNull();
        worker.ShouldNotContain(l => l.Trim() == $"{DeployService}:");
        Deploy.Preamble.ShouldNotContain(l => l.Contains($"{KeyVariable}:", StringComparison.Ordinal));

        // The control: the same read finds the durable instance's key where x-app-connections declares it.
        Deploy.Preamble.ShouldContain(l => l.Contains("ConnectionStrings__Redis:", StringComparison.Ordinal));
    }

    [Fact]
    public void DevApi_VolatileRedisPort_IsTheLoopbackPortTheDevInstancePublishes()
    {
        // The dev Api reads a COMMITTED value, not an environment variable, so the pair that has to agree is
        // appsettings.Development.json and the port the dev service publishes.
        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "src", "Jobbliggaren.Api", "appsettings.Development.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var configured = settings.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty(DependencyInjection.VolatileRedisConnectionStringName)
            .GetString();

        configured.ShouldNotBeNull().ShouldStartWith("localhost:");
        var port = configured["localhost:".Length..];

        ComposeFile.ListUnder(Dev.ServiceBlock(DevService), "ports")
            .ShouldHaveSingleItem()
            .ShouldBe($"\"127.0.0.1:{port}:6379\"");
    }
}
