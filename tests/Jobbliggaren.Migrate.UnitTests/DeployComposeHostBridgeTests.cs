using Jobbliggaren.Infrastructure.Admin.HostBridge;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1982 (ADR 0157) — the one mount that lets the Api read what the host sampler publishes, and the
/// setting that tells it where.
///
/// <para>
/// The registration is deliberately not fail-fast (an unset directory is a valid state the Backup card
/// reports as not observed), so a deploy that forgets the mount or the variable fails OPEN and quietly.
/// This file closes that, the way <see cref="DeployComposeDataProtectionTests"/> does for the keyring.
/// </para>
///
/// <para>
/// The other half of the contract is what must NOT be true: the directory is read-only, it is mounted
/// into <c>api</c> and nothing else, and it is not a parent of anything secret. Every assertion is scoped
/// to a service block, never to the file, because an <c>x-*</c> anchor hoisted into the preamble would be
/// inherited by a second service while a file-wide count still read as one.
/// </para>
/// </summary>
public class DeployComposeHostBridgeTests
{
    private const string Source = "/run/jobbliggaren/observations";
    private const string Target = "/run/observations";

    private static readonly string DirectoryVariable = HostBridgeOptions.DirectoryConfigKey.Replace(":", "__");
    private static readonly string[] SecretDirectories = ["/run/jobbliggaren/secrets", "/run/jobbliggaren/host-secrets"];

    private const string ComposePath = "deploy/docker-compose.yml";

    private static ComposeFile Compose => new(ComposePath);

    private static bool Names(List<string> mount, string source) =>
        mount.Any(l => l == $"source: {source}" || l.StartsWith($"{source}:", StringComparison.Ordinal));

    [Fact]
    public void Api_MountsTheObservationsDirectory_ReadOnly_AtTheTargetItIsToldAbout()
    {
        var api = Compose.ServiceBlock("api");
        var mounts = ComposeMounts.Of(api).Where(m => Names(m, Source)).ToList();

        mounts.Count.ShouldBe(1, $"api must mount {Source} exactly once");
        var mount = mounts[0];
        mount.ShouldContain("type: bind");
        mount.ShouldContain($"target: {Target}");
        mount.ShouldContain("read_only: true", "the API reads what the host publishes and can never write to it");
        mount.ShouldContain("create_host_path: true",
            "this directory is not secret, and a recreate of api that runs before it exists must not stop the application from starting");

        ComposeFile.Setting(api.Where(l => l.StartsWith("      ", StringComparison.Ordinal)).ToList(), DirectoryVariable)
            .ShouldBe(Target, $"{DirectoryVariable} is derived from HostBridgeOptions.DirectoryConfigKey and must name the mount's target");
    }

    [Fact]
    public void NoOtherServiceMountsTheDirectory_OrIsToldWhereItIs()
    {
        var others = ComposeMounts.ServiceNames(ComposePath).Where(name => name != "api").ToList();
        others.ShouldContain("worker");

        foreach (var service in others)
        {
            var block = Compose.ServiceBlock(service);
            string.Join('\n', block.Where(l => !l.TrimStart().StartsWith('#'))).Contains("observations", StringComparison.Ordinal)
                .ShouldBeFalse($"{service} must not mount or name the host-bridge directory");
            string.Join('\n', block).Contains(DirectoryVariable, StringComparison.Ordinal)
                .ShouldBeFalse($"{service} must not carry {DirectoryVariable}; only the Api reads the host bridge");
        }
    }

    [Fact]
    public void ThePreamble_HoistsNothingAboutTheDirectory_IntoAnAnchorAnotherServiceCouldInherit() =>
        string.Join('\n', Compose.Preamble.Where(l => !l.TrimStart().StartsWith('#')))
            .Contains("observations", StringComparison.Ordinal)
            .ShouldBeFalse("an x-* anchor is inherited by every service that merges it");

    [Fact]
    public void NoServiceMountsAParentOfASecretDirectory_AndNoneMountsTheCredentialDirectoryAtAll()
    {
        SecretDirectories.ShouldAllBe(secret => !secret.StartsWith(Source + "/", StringComparison.Ordinal) && !Source.StartsWith(secret + "/", StringComparison.Ordinal));

        foreach (var service in ComposeMounts.ServiceNames(ComposePath))
        {
            foreach (var mount in ComposeMounts.Of(Compose.ServiceBlock(service)))
            {
                var source = ComposeMounts.SourceOf(mount);
                if (source is null)
                {
                    continue;
                }

                source.ShouldNotBe("/run/jobbliggaren/host-secrets", $"{service} mounts the backup credential directory, which is deliberately mounted nowhere");
                foreach (var secret in SecretDirectories)
                {
                    secret.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal).ShouldBeFalse(
                        $"{service} mounts {source}, a parent of {secret}");
                }
            }
        }
    }

    [Fact]
    public void TheSource_IsTheDirectoryTheSamplerWrites_ItsTmpfilesLineAndItsLibraryAgree()
    {
        var tmpfiles = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "deploy/systemd/jobbliggaren-observe-tmpfiles.conf"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .ToList();
        tmpfiles.Count.ShouldBe(1);
        tmpfiles[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].ShouldBe(Source);

        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "deploy/systemd/jobbliggaren-observe-lib.sh"))
            .ShouldContain($"readonly OBSERVATIONS_DIR={Source}");
    }
}
