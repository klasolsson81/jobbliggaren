using Jobbliggaren.Infrastructure;
using Jobbliggaren.TestSupport;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The non-persisted Redis as the DEPLOY STACK declares it: built from <c>deploy/docker-compose.yml</c>'s
/// own <c>image:</c>, <c>command:</c>, <c>read_only:</c>, <c>tmpfs:</c>, <c>mem_limit:</c> and
/// <c>memswap_limit:</c>, never from a hand-written command line. A probe on hand-written flags measures
/// the test's configuration, not the box's (AGENTS.md §5 <c>Tests:</c>). The form of those fields is pinned
/// by <c>DeployComposeVolatileRedisTests</c>; what they DO is measured on containers built here.
/// </summary>
internal static class VolatileRedisContainer
{
    internal const string DeployService = "redis-volatile";
    private const string DeployFile = "deploy/docker-compose.yml";
    private const long Mebibyte = 1024 * 1024;

    /// <summary>The environment form of the connection string, derived from the constant the Api reads.</summary>
    internal static readonly string ConnectionStringVariable =
        "ConnectionStrings__" + DependencyInjection.VolatileRedisConnectionStringName;

    internal static RedisContainer FromDeployCompose()
    {
        var block = new ComposeFile(DeployFile).ServiceBlock(DeployService);

        var image = ComposeFile.Setting(block, "image")
            ?? throw new InvalidOperationException($"`{DeployService}` declares no image.");
        var declared = ComposeFile.ListUnder(block, "command").Select(Unquote).ToList();
        var readOnly = ComposeFile.Setting(block, "read_only") == "true";
        var tmpfs = ComposeFile.ListUnder(block, "tmpfs")
            .Select(m => m.Split(':', 2))
            .ToDictionary(m => m[0], m => m.Length > 1 ? m[1] : string.Empty);
        var memory = Bytes(ComposeFile.Setting(block, "mem_limit"));
        var memorySwap = Bytes(ComposeFile.Setting(block, "memswap_limit"));

        return new RedisBuilder(image)
            .WithCommand([.. declared])
            .WithCreateParameterModifier(parameters =>
            {
                var host = parameters.HostConfig ??= new Docker.DotNet.Models.HostConfig();
                host.ReadonlyRootfs = readOnly;
                host.Tmpfs = tmpfs;
                host.Memory = memory;
                host.MemorySwap = memorySwap;
            })
            .Build();
    }

    // The compose file quotes the two arguments YAML would otherwise retype: "" and "no".
    private static string Unquote(string scalar) =>
        scalar.Length >= 2 && scalar[0] == '"' && scalar[^1] == '"' ? scalar[1..^1] : scalar;

    private static long Bytes(string? mebibytes) =>
        mebibytes is null ? 0 : ComposeFile.Mebibytes(mebibytes) * Mebibyte;
}
