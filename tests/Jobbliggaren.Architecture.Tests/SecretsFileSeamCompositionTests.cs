using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #198 / ADR 0050 gate B-1 — **both** composition roots must read secrets from files, and the
/// file source must be registered **last**.
///
/// <para>
/// The compose anchor guarantees that `api` and `worker` MOUNT the same directory; nothing
/// guaranteed that both HOSTS read it. Deleting
/// <c>builder.Configuration.AddEnvFileSecrets()</c> from one of the two <c>Program.cs</c> files
/// left the entire unit suite green — measured, and reported independently by
/// `dotnet-architect` and `code-reviewer` on PR #1262.
/// </para>
///
/// <para>
/// <b>Why source text and not NetArchTest.</b> NetArchTest reads type references, and a type
/// reference cannot express ORDER — but order is half the invariant here: registered anywhere
/// but last, a stray container environment variable outranks the tmpfs file and recreates the
/// <c>docker inspect</c> surface #198 removed. One ten-line source assertion kills both
/// mutations (the deletion and a later <c>Configuration.Add*</c> appended after it); a type-
/// reference test kills only the first. Source-text pinning has precedent in this repo, and
/// this costs no <c>WebApplicationFactory</c> — the Api suite already sits one host below EF's
/// process-global ceiling (#1190).
/// </para>
///
/// <para>
/// CLAUDE.md §11 names this exact class for <c>ReverseProxyOptions</c>: <i>"the pin covers the
/// constant, not the composition root, so a fallback bind added in Program.cs would still
/// pass."</i> There the fourth host was declined deliberately and the gap noted as a miss. Here
/// the call site alone is the whole invariant, so the miss does not have to be inherited.
/// </para>
/// </summary>
public class SecretsFileSeamCompositionTests
{
    private const string SeamCall = "AddEnvFileSecrets()";
    private const string ConfigurationAdd = "builder.Configuration.Add";

    public static TheoryData<string, string> CompositionRoots() => new()
    {
        { "Api", "src/Jobbliggaren.Api/Program.cs" },
        { "Worker", "src/Jobbliggaren.Worker/Program.cs" },
    };

    [Theory]
    [MemberData(nameof(CompositionRoots))]
    public void Composition_root_registers_the_secrets_file_seam_last(string host, string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        File.Exists(path).ShouldBeTrue($"expected {host}'s composition root at {relativePath}");

        var configurationAddLines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(ConfigurationAdd, StringComparison.Ordinal))
            .ToList();

        configurationAddLines.ShouldNotBeEmpty(
            $"{host} registers no configuration sources at all — that cannot be right.");

        var seamCallCount = configurationAddLines
            .Count(line => line.Contains(SeamCall, StringComparison.Ordinal));

        seamCallCount.ShouldBe(1,
            $"the {host} host must call {SeamCall} exactly once. Without it the master key " +
            "can only arrive as a container environment value, which is the docker-inspect " +
            "surface #198 removed (gate B-1).");

        var lastRegistration = configurationAddLines[^1];
        lastRegistration.Contains(SeamCall, StringComparison.Ordinal).ShouldBeTrue(
            $"{SeamCall} must be the LAST configuration source {host} registers, but the last " +
            $"one is: {lastRegistration}. .NET configuration is last-source-wins, so a source " +
            "added after it would outrank the tmpfs file — letting a stray container " +
            "environment variable supply the master key and undoing gate B-1 silently.");
    }

    // Walks up from the test binary to the repository root. The suite runs from
    // tests/<project>/bin/<config>/<tfm>/, and both the solution file and the src/ directory
    // identify the root unambiguously.
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
