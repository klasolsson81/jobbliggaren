using Jobbliggaren.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1350 — pins that the deploy stack actually persists the Api's Data-Protection keyring.
///
/// <para>
/// The registration is deliberately optional: unset means the framework default, so a dev boot is
/// unchanged and no fail-fast key is introduced (CLAUDE.md §11 is not triggered). The price of that
/// choice is that a deployment which forgets the value fails OPEN and silently — the keyring goes
/// back into the container's writable layer and every outstanding activation, password-reset and
/// change-email link dies on the next recreate, with the user seeing a response indistinguishable
/// from an expired token. This file is what closes that, instead of a boot refusal that would break
/// every dev machine to protect one host.
/// </para>
///
/// <para>
/// Text assertions against the compose file, for the reason <see cref="DeployComposeRoleTests"/>
/// states: a compose file is not .NET configuration, and the values under test ARE literals. The
/// code half — that the Api honours the key at all, and pins its discriminator — is
/// <c>AddApiDataProtectionGateTests</c> in the Api integration tests, which is where
/// <c>Jobbliggaren.Infrastructure</c> is referenced. Neither half means much alone.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeDataProtectionTests
{
    /// <summary>
    /// Derived from the constant the Api reads, not restated. `Jobbliggaren.Migrate` references
    /// `Jobbliggaren.Infrastructure`, so this project already has the constant on its compile
    /// surface — and a third hand-written copy of the string is exactly what would let a rename
    /// pass both halves of the pin while un-persisting the keyring. ASP.NET Core's environment
    /// variable provider maps `__` to `:`.
    /// </summary>
    private static readonly string KeyPathVariable =
        DependencyInjection.DataProtectionKeyPathConfigKey.Replace(":", "__");

    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"))
            .Split('\n');

    private static string SingleLineContaining(string needle)
    {
        // Not SingleOrDefault: it returns null on zero and THROWS on two or more, so the multi-match
        // case would surface LINQ's own message while this one claims "has no single line".
        var matches = ComposeLines.Where(l => l.Contains(needle, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"deploy/docker-compose.yml has {matches.Count} lines containing '{needle}', expected " +
                "exactly 1. If the file was restructured, this pin must be rewritten rather than deleted.");
        return matches[0];
    }

    /// <summary>The value the Api is told to persist its keyring to.</summary>
    private static string DeclaredKeyPath =>
        SingleLineContaining($"{KeyPathVariable}:").Split(':', 2)[1].Trim();

    [Fact]
    public void Api_DeclaresAKeyPath_SoTheKeyringLeavesTheContainerWritableLayer()
    {
        DeclaredKeyPath.ShouldNotBeNullOrWhiteSpace(
            customMessage:
            "An empty value is read as unset by the Api, which falls back to the framework default " +
            "inside the container. That is the #1350 defect, and it is silent at both ends.");

        // Absolute, because it is a container path the mount below has to match exactly.
        DeclaredKeyPath.ShouldStartWith("/");
    }

    [Fact]
    public void Api_MountsAWritableVolumeAtTheDeclaredKeyPath()
    {
        var mount = ComposeLines
            .Select(l => l.Trim())
            .SingleOrDefault(l => l.StartsWith("- ", StringComparison.Ordinal)
                                  && l.EndsWith($":{DeclaredKeyPath}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No volume is mounted at {DeclaredKeyPath}. The Api would write its keyring into " +
                "the container's writable layer, which is exactly what #1350 is about — and it " +
                "would do so without an error, because a missing mount is not a failure.");

        // The read-only flag is the failure mode that looks like success: the mount exists, the
        // path is right, and the app cannot write a key. Asserted on the destination suffix, so a
        // `:ro` appended after it fails here rather than at 03:00 on the box.
        mount.ShouldNotContain(":ro",
            customMessage:
            "The keyring is WRITTEN by the app, unlike the secrets mount which is injected. A " +
            "read-only mount here fails at key-generation time, not at boot.");

        // A named volume rather than a host bind. `/run/jobbliggaren/` is RAM-backed (ADR 0050
        // B-1), so a bind under it would lose the keyring on every reboot while looking repaired.
        var source = mount[2..].Split(':', 2)[0];
        source.ShouldNotStartWith("/",
            customMessage:
            "A host bind path here reintroduces a first-boot precondition nothing enforces, and " +
            "under /run/ it would be RAM-backed and lost on reboot.");
        SingleLineContaining($"  {source}:").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ApiImage_OwnsTheMountPoint_SoTheNonRootUserCanWriteAKey()
    {
        // The failure mode that survives every other pin in this file. A named volume mounted over
        // a path the image does not have is created root:root 0755 — Docker only copies and chowns
        // when the image has content at the destination — and the Api runs as a non-root user.
        // Measured 2026-08-19 against a fresh empty volume: without the chown, `drwxr-xr-x root
        // root` and `Permission denied`; with it, `drwxr-xr-x app app` and a successful write.
        //
        // It is invisible where anyone would look: FileSystemXmlRepository's constructor only calls
        // Directory.Create(), a no-op on an existing directory, so boot and /api/ready are green and
        // the UnauthorizedAccessException lands on the FIRST key generation — the first registration
        // or password reset. Both mandatory reviewers found this independently on PR #1408.
        var lines = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, "src", "Jobbliggaren.Api", "Dockerfile"))
            .Select(l => l.Trim()).ToList();

        var chown = lines.SingleOrDefault(
            l => l.StartsWith("RUN ", StringComparison.Ordinal)
                 && l.Contains("chown", StringComparison.Ordinal)
                 && l.Contains(DeclaredKeyPath, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"src/Jobbliggaren.Api/Dockerfile does not chown {DeclaredKeyPath}. The compose file " +
                "mounts a writable volume there and the container is non-root, so the keyring cannot " +
                "be written — silently, until the first token mint.");

        // Before USER, or it runs unprivileged and cannot chown.
        var userLine = lines.FindIndex(l => l.StartsWith("USER ", StringComparison.Ordinal)
                                            && !l.Contains("root", StringComparison.Ordinal));
        userLine.ShouldBeGreaterThan(-1);
        lines.IndexOf(chown).ShouldBeLessThan(userLine,
            customMessage: "The chown must run before the image drops to the non-root user.");
    }

    [Fact]
    public void Worker_IsNotGivenTheKeyPath_SoItNeverSharesTheApiKeyring()
    {
        // `AddCoreIdentityForWorker` registers no IDataProtectionProvider, and the sole consumer in
        // src/ is PasswordResetTokenProvider's constructor — Api only. A shared keyring would hand
        // the Worker cryptographic reach over tokens it never mints or validates, and re-open the
        // cross-process coupling the 2026-07-10 ruling rejected. One occurrence in the whole file
        // is therefore the invariant, not merely the current state.
        ComposeLines.Count(l => l.Contains($"{KeyPathVariable}:", StringComparison.Ordinal))
            .ShouldBe(1);
    }
}
