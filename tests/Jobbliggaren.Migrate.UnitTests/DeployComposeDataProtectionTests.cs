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
    private const string KeyPathVariable = "DataProtection__KeyPath";

    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"))
            .Split('\n');

    private static string SingleLineContaining(string needle) =>
        ComposeLines.SingleOrDefault(l => l.Contains(needle, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"deploy/docker-compose.yml has no single line containing '{needle}'. If the file was " +
            "restructured, this pin must be rewritten rather than deleted.");

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
