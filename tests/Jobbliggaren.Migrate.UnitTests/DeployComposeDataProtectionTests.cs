using Jobbliggaren.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1350 — pins that the deploy stack actually persists the Api's Data-Protection keyring.
///
/// <para>
/// The registration is deliberately optional (unset means the framework default), so a deployment
/// that forgets the value fails OPEN and silently. This file is what closes that, instead of a boot
/// refusal that would break every dev machine to protect one host. The code half — that the Api
/// honours the key at all — is <c>AddApiDataProtectionGateTests</c>, the project where
/// <c>Jobbliggaren.Infrastructure</c> is referenced from a host-capable test. Neither half means
/// much alone.
/// </para>
///
/// <para>
/// Every assertion here is scoped to the <c>api</c> SERVICE BLOCK, never to the file. That is not
/// caution: <see cref="DeployComposeIngestGateTests"/> records the same trap being measured and
/// closed once already — hoisting a key into one of this file's <c>x-*</c> anchors leaves it
/// parsing, binding and reading as configured while a second service silently inherits it, and a
/// file-global occurrence count calls that arrangement green.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeDataProtectionTests
{
    /// <summary>
    /// Derived from the constant the Api reads, not restated. <c>Jobbliggaren.Migrate</c> references
    /// <c>Jobbliggaren.Infrastructure</c>, so this project already has the constant on its compile
    /// surface — and a third hand-written copy of the string is what would let a rename pass both
    /// halves of the pin while un-persisting the keyring. ASP.NET Core's environment-variable
    /// provider maps <c>__</c> to <c>:</c>.
    /// </summary>
    private static readonly string KeyPathVariable =
        DependencyInjection.DataProtectionKeyPathConfigKey.Replace(":", "__");

    private static string[] ComposeLines =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"));

    /// <summary>A top-level service key, i.e. the end of the previous service's block.</summary>
    private static bool IsTwoSpaceKey(string line) =>
        line.Length > 2
        && line.StartsWith("  ", StringComparison.Ordinal)
        && line[2] != ' '
        && line.TrimEnd().EndsWith(':');

    /// <summary>The lines belonging to one service, exclusive of the next service's key.</summary>
    private static List<string> ServiceBlock(string service)
    {
        var lines = ComposeLines;
        var start = Array.FindIndex(lines, l => l.StartsWith($"  {service}:", StringComparison.Ordinal));
        start.ShouldBeGreaterThan(-1, $"the compose file no longer declares a `{service}` service");

        var end = Array.FindIndex(lines, start + 1, IsTwoSpaceKey);
        if (end < 0) end = lines.Length;
        return lines[(start + 1)..end].ToList();
    }

    private static string SingleLineContaining(IReadOnlyList<string> scope, string needle, string where)
    {
        // Not SingleOrDefault: it returns null on zero and THROWS on two or more, so the multi-match
        // case would surface LINQ's own message while this one claims there was none.
        var matches = scope.Where(l => l.Contains(needle, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"{where} has {matches.Count} lines containing '{needle}', expected exactly 1. If the " +
                "file was restructured, this pin must be rewritten rather than deleted.");
        return matches[0];
    }

    /// <summary>The value the api service is told to persist its keyring to.</summary>
    private static string DeclaredKeyPath =>
        SingleLineContaining(ServiceBlock("api"), $"{KeyPathVariable}:", "the api service block")
            .Split(':', 2)[1].Trim();

    [Fact]
    public void Api_DeclaresAKeyPath_SoTheKeyringLeavesTheContainerWritableLayer()
    {
        // Empty reads as unset to the Api, which falls back to the container's writable layer — the
        // #1350 defect, silent at both ends. Absolute, because the mount has to match it exactly.
        DeclaredKeyPath.ShouldNotBeNullOrWhiteSpace();
        DeclaredKeyPath.ShouldStartWith("/");
    }

    [Fact]
    public void Api_MountsAWritableVolumeAtTheDeclaredKeyPath()
    {
        var path = DeclaredKeyPath;
        var mount = SingleLineContaining(ServiceBlock("api"), $":{path}", "the api service block").Trim();

        // Selected on Contains and asserted separately, so a `:ro` suffix fails on the read-only
        // assertion with its own message instead of falling out of the selector as "not mounted".
        mount.ShouldStartWith("- ");
        mount.ShouldNotContain(":ro",
            customMessage:
            "The keyring is WRITTEN by the app, unlike the secrets mount which is injected. A " +
            "read-only mount fails at key-generation time, not at boot.");
        mount.ShouldEndWith($":{path}");

        // A named volume, not a host bind: /run/jobbliggaren/ is RAM-backed (ADR 0050 B-1), so a
        // bind under it would lose the keyring on every reboot while looking repaired.
        var source = mount[2..].Split(':', 2)[0];
        source.ShouldNotStartWith("/");
        ComposeLines.Count(l => l.TrimEnd() == $"  {source}:").ShouldBe(1,
            customMessage: $"the named volume `{source}` is mounted but never declared");
    }

    [Fact]
    public void ApiImage_OwnsTheMountPoint_SoTheNonRootUserCanWriteAKey()
    {
        // The failure mode every other pin here survives. A named volume mounted over a path the
        // image does not have is created root:root 0755 — Docker only propagates ownership when the
        // image has content at the destination — and the Api runs non-root. Measured 2026-08-19
        // against a fresh empty volume: without the chown, root-owned and "Permission denied"; with
        // it, app-owned and a successful write. Invisible where anyone would look, because
        // FileSystemXmlRepository's constructor only calls Directory.Create().
        var path = DeclaredKeyPath;
        var lines = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, "src", "Jobbliggaren.Api", "Dockerfile"))
            .Select(l => l.Trim()).ToList();

        var chown = lines.SingleOrDefault(
            l => l.StartsWith("RUN ", StringComparison.Ordinal)
                 && l.Contains("chown", StringComparison.Ordinal)
                 && l.Contains(path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"src/Jobbliggaren.Api/Dockerfile does not chown {path}. Compose mounts a writable " +
                "volume there and the container is non-root, so the keyring cannot be written — " +
                "silently, until the first token mint.");

        var dropsPrivilege = lines.FindIndex(
            l => l.StartsWith("USER ", StringComparison.Ordinal)
                 && !l.Contains("root", StringComparison.Ordinal));
        dropsPrivilege.ShouldBeGreaterThan(-1);
        lines.IndexOf(chown).ShouldBeLessThan(dropsPrivilege,
            customMessage: "The chown must run before the image drops to the non-root user.");

        // The fourth axis, and the one the first version of this pin left open: path and
        // order were bound, the PRINCIPAL was not. `chown nobody:nobody /keys` before
        // `USER app` passes both assertions above and is broken in exactly the way this
        // whole PR is about (dotnet-architect, PR #1408 re-check).
        var owner = chown.Split("chown ", 2)[1].TrimStart().Split(' ', 2)[0].Split(':')[0];
        var runsAs = lines[dropsPrivilege]["USER ".Length..].Trim();
        owner.ShouldBe(runsAs,
            customMessage:
            $"The image chowns {path} to '{owner}' but runs as '{runsAs}'. Ownership by any " +
            "other principal fails at the first key generation, not at boot.");
    }

    [Fact]
    public void Worker_IsNotGivenTheKeyPath_SoItNeverSharesTheApiKeyring()
    {
        // AddCoreIdentityForWorker registers no IDataProtectionProvider, so a shared keyring would
        // hand the Worker cryptographic reach over tokens it never mints or validates.
        ServiceBlock("worker")
            .ShouldNotContain(l => l.Contains($"{KeyPathVariable}:", StringComparison.Ordinal));

        // The anchor half is the one a file-global count cannot see: hoisted into x-app-secrets the
        // key would still occur exactly once, and BOTH services would merge it.
        ComposeLines
            .TakeWhile(l => !l.StartsWith("services:", StringComparison.Ordinal))
            .ShouldNotContain(l => l.Contains($"{KeyPathVariable}:", StringComparison.Ordinal));
    }
}
