using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Pins the set of test classes that still start a container PER TEST — a class implementing
/// <see cref="IAsyncLifetime"/> itself, building its own Postgres or Redis, and joining no shared
/// fixture. xunit instantiates such a class once per test method, so every test pays a container
/// start (and, for Postgres, every migration). Measured 2026-09-24 on the runner (#1785): that shape
/// carried 79 % of this project's 17.6 minutes for 21 % of its tests.
///
/// <para>
/// The allowlist is an equality, not a floor: a class that drops off it is removed here in the same
/// PR, and a new one is added only with the reason it cannot share <see cref="SharedPostgresFixture"/>
/// or a <see cref="SharedVolatileRedisFixture"/>/<see cref="SharedPlainRedisFixture"/>. The
/// classifier reads this project's own source tree, the same way <c>VolatileRedisContainer</c> reads
/// the deploy compose file: a repo fact, measured where it lives.
/// </para>
/// </summary>
public sealed partial class OwnContainerPerTestTests
{
    // Each entry names why the class keeps its own container.
    private static readonly string[] Allowed =
    [
        // Fills the instance to its memory limit; the contract under test IS the container's own limit.
        "Auth/VolatileRedisOutOfMemoryTests.cs",
        // Probes that nothing survives a restart of ITS container.
        "Auth/VolatileRedisPersistenceProbeTests.cs",
        // One test each: boots a Production host on process-global environment variables.
        "Configuration/IdempotentAdminRoleSeederProdBubbleTests.cs",
        "Configuration/TaxonomySnapshotSeederProdBubbleTests.cs",
        // 71 tests. Converted in the follow-up after PR #1837 (which edits the file) has merged.
        "JobAds/RecruiterErasureIngestTests.cs",
        // Every test stops its Redis.
        "Sessions/RedisSessionStoreFailureTests.cs",
    ];

    [Fact]
    public void Only_the_allowlisted_classes_start_a_container_per_test()
    {
        var projectDirectory = ProjectDirectory();
        var actual = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => StartsAContainerPerTest(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(projectDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(
            Allowed.Order(StringComparer.Ordinal).ToList(),
            "a test class with its own container per test is either allowlisted above with its reason, "
            + "or joins SharedPostgresFixture / a SharedRedisFixture (Infrastructure/).");
    }

    private static bool StartsAContainerPerTest(string source) =>
        HasTests().IsMatch(source)
        && ClassImplementsAsyncLifetime().IsMatch(source)
        && BuildsAContainer().IsMatch(source)
        && !source.Contains("[Collection(", StringComparison.Ordinal)
        && !source.Contains("IClassFixture<", StringComparison.Ordinal);

    private static string ProjectDirectory([CallerFilePath] string thisFile = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(thisFile))
        ?? throw new InvalidOperationException("The test file's project directory could not be resolved.");

    [GeneratedRegex(@"\[(Fact|Theory)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HasTests();

    [GeneratedRegex(@"class\s+\w+\s*(?:\([^)]*\))?\s*:[^{]*\bIAsyncLifetime\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClassImplementsAsyncLifetime();

    [GeneratedRegex(@"new PostgreSqlBuilder\(|new RedisBuilder\(|VolatileRedisContainer\.FromDeployCompose\(\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuildsAContainer();
}
