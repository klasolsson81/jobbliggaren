using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Configuration;

/// <summary>
/// #198 / ADR 0050 gate B-1 — the <c>&lt;KEY&gt;_FILE</c> configuration source that lets the
/// master key and the three peppers reach Api/Worker as FILES rather than as container
/// environment values (Docker persists environment to disk in its own container state, and
/// <c>docker inspect</c> returned the key after the container had exited — measured on the box
/// 2026-08-05, #1240).
///
/// <para>
/// Two layers are pinned here, deliberately. The pure <c>Resolve</c> cases cover the policy;
/// the composition cases at the bottom drive a real <see cref="ConfigurationBuilder"/> through
/// <c>AddEnvFileSecrets</c> → <c>Build</c> → <c>Load</c>, because the last-source-wins
/// precedence is a security property and an earlier version of this file left it unpinned:
/// swapping <c>Add</c> for <c>Sources.Insert(0, …)</c> would have inverted it silently, letting
/// a stray container environment variable outrank the tmpfs file and recreating exactly the
/// surface #198 removed.
/// </para>
/// </summary>
public class EnvFileSecretsConfigurationTests
{
    private static Dictionary<string, string?> Resolve(
        IEnumerable<KeyValuePair<string, string?>> environment,
        Func<string, string> readFile) =>
        EnvFileSecretsConfigurationProvider.Resolve(environment, readFile);

    private static KeyValuePair<string, string?> Env(string name, string? value) => new(name, value);

    [Fact]
    public void FileSuffixedVariable_ResolvesConfigurationKeyFromFileContent()
    {
        var data = Resolve(
            [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/run/app-secrets/master")],
            _ => "c2VjcmV0");

        // `__` is the section delimiter, and the `_FILE` marker is stripped — so the compose
        // pointer lands on exactly the key FieldEncryptionOptions binds.
        data.ShouldContainKeyAndValue("FieldEncryption:LocalMasterKeyBase64", "c2VjcmV0");
    }

    [Fact]
    public void FileContent_IsTrimmed()
    {
        // Write hygiene and the empty-file discriminator. NOT an HMAC-correctness control: all
        // four crypto values are consumed through Convert.FromBase64String, which ignores
        // whitespace (measured 2026-08-09). The control is writing exactly the intended bytes.
        var data = Resolve(
            [Env("AuditPseudonymization__PepperBase64_FILE", "/run/app-secrets/pepper")],
            _ => "  cGVwcGVy\n");

        data.ShouldContainKeyAndValue("AuditPseudonymization:PepperBase64", "cGVwcGVy");
    }

    [Fact]
    public void NoFileVariables_ProviderContributesNothing()
    {
        // THE DEV-UNCHANGED PIN (CLAUDE.md §11). With no *_FILE variables set the source is
        // inert, so appsettings.Local.json keeps working exactly as before and no new
        // mandatory dev key is introduced.
        var data = Resolve(
            [Env("PATH", "/usr/bin"), Env("FieldEncryption__LocalMasterKeyBase64", "inline")],
            _ => throw new InvalidOperationException("no file must be read"));

        data.ShouldBeEmpty();
    }

    [Fact]
    public void EmptyFile_ContributesNothing_SoTheValidatorOwnsTheVerdict()
    {
        // One error, one owner: "this secret is missing" belongs to
        // FieldEncryptionOptionsValidator, which already fails startup in ALL environments.
        var data = Resolve(
            [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/run/app-secrets/master")],
            _ => "   \n");

        data.ShouldBeEmpty();
    }

    [Fact]
    public void UnreadableFile_Throws_AndNamesPathButNeverContent()
    {
        const string path = "/run/app-secrets/master";
        const string secret = "the-actual-secret-value";

        var ex = Should.Throw<InvalidOperationException>(() => Resolve(
            [Env("FieldEncryption__LocalMasterKeyBase64_FILE", path)],
            // Measured 2026-08-09: File.ReadAllText's own exceptions carry the path, never the
            // content. This stub is a mutation-detectability device for the "propagate
            // ex.Message" edit, not a claim about the current adapter.
            _ => throw new IOException($"device error while reading {secret}")));

        ex.Message.ShouldContain(path);
        ex.Message.ShouldContain("FieldEncryption__LocalMasterKeyBase64_FILE");
        // CLAUDE.md §5: never secret material in an exception message.
        ex.Message.ShouldNotContain(secret);
    }

    [Fact]
    public void BlankPointer_IsNotConfigured_RatherThanAnError()
    {
        var data = Resolve(
            [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "   ")],
            _ => throw new InvalidOperationException("no file must be read"));

        data.ShouldBeEmpty();
    }

    [Fact]
    public void BareSuffixVariable_IsIgnored()
    {
        var data = Resolve([Env("_FILE", "/run/app-secrets/master")], _ => "value");

        data.ShouldBeEmpty();
    }

    [Fact]
    public void SuffixMustBeAtTheEnd_NotAnywhereInTheName()
    {
        var data = Resolve(
            [Env("FieldEncryption___FILE_Something", "/run/app-secrets/master")],
            _ => "value");

        data.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("SSL_CERT_FILE")]
    [InlineData("REQUESTS_CA_BUNDLE_FILE")]
    [InlineData("SOME_FILE")]
    public void UnqualifiedNames_AreNotOursAndAreNeverRead(string variableName)
    {
        // Without the `__` requirement this source would read every incidental *_FILE variable
        // in the ambient environment. SSL_CERT_FILE is the common one in Linux containers, and
        // one pointing at an unreadable path would refuse the host's boot for a file that has
        // nothing to do with this application. The delegate throwing proves it is never opened.
        var data = Resolve(
            [Env(variableName, "/etc/ssl/certs/ca-certificates.crt")],
            _ => throw new InvalidOperationException("an unqualified name must never be read"));

        data.ShouldBeEmpty();
    }

    [Fact]
    public void CaseOnlyCollision_IsRefused_RatherThanDecidedByEnumerationOrder()
    {
        // Configuration keys are case-insensitive, so these two resolve to one key. For ordinary
        // configuration last-wins is framework parity; for a secret it is a coin toss decided by
        // Hashtable enumeration order, which is unspecified.
        var ex = Should.Throw<InvalidOperationException>(() => Resolve(
            [
                Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/a"),
                Env("fieldencryption__localmasterkeybase64_FILE", "/b"),
            ],
            path => $"value-from{path}"));

        ex.Message.ShouldContain("FieldEncryption:LocalMasterKeyBase64");
    }

    [Fact]
    public void EveryProductionSecret_ResolvesThroughTheSameSeam()
    {
        // All five files the box injects, in one pass: the seam is key-agnostic, which is why
        // the three peppers cost no code (senior-cto-advisor bind 2026-08-09, Q2).
        var data = Resolve(
            [
                Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/s/master"),
                Env("FieldEncryption__LocalMasterKeyId_FILE", "/s/id"),
                Env("AuditPseudonymization__PepperBase64_FILE", "/s/audit"),
                Env("CompanyWatchPseudonymization__PepperBase64_FILE", "/s/watch"),
                Env("CvReviewFingerprintPseudonymization__PepperBase64_FILE", "/s/cv"),
            ],
            path => $"value-from{path}");

        data.Count.ShouldBe(5);
        data.ShouldContainKeyAndValue("FieldEncryption:LocalMasterKeyBase64", "value-from/s/master");
        data.ShouldContainKeyAndValue("FieldEncryption:LocalMasterKeyId", "value-from/s/id");
        data.ShouldContainKeyAndValue("AuditPseudonymization:PepperBase64", "value-from/s/audit");
        data.ShouldContainKeyAndValue("CompanyWatchPseudonymization:PepperBase64", "value-from/s/watch");
        data.ShouldContainKeyAndValue(
            "CvReviewFingerprintPseudonymization:PepperBase64", "value-from/s/cv");
    }

    // ---- Composition: AddEnvFileSecrets -> Build -> Load, through a real builder -------------
    //
    // These drive the wiring the pure cases above cannot reach. No host, no WebApplicationFactory
    // (the Api suite sits one below EF's process-global ceiling, #1190) and no dependency on the
    // machine's own environment.

    private static IConfigurationBuilder WithSeam(
        IConfigurationBuilder builder,
        IEnumerable<KeyValuePair<string, string?>> environment,
        Func<string, string> readFile) =>
        builder.Add(new EnvFileSecretsConfigurationSource(() => environment, readFile));

    [Fact]
    public void Composition_FileValueReachesConfiguration()
    {
        var config = WithSeam(
                new ConfigurationBuilder(),
                [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/run/app-secrets/master")],
                _ => "from-file")
            .Build();

        config["FieldEncryption:LocalMasterKeyBase64"].ShouldBe("from-file");
    }

    [Fact]
    public void Composition_FileOutranksAnEarlierSource()
    {
        // THE PRECEDENCE PIN, and it is a security property rather than a preference. Registered
        // last, the tmpfs file wins over any stray container environment variable. Inverted —
        // e.g. Sources.Insert(0, ...) — an environment-set master key would outrank the file and
        // recreate the docker-inspect surface #198 removed.
        var config = WithSeam(
                new ConfigurationBuilder().AddInMemoryCollection(
                    [new("FieldEncryption:LocalMasterKeyBase64", "from-environment")]),
                [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "/run/app-secrets/master")],
                _ => "from-file")
            .Build();

        config["FieldEncryption:LocalMasterKeyBase64"].ShouldBe("from-file");
    }

    [Fact]
    public void Composition_WithNoFileVariables_LeavesEarlierSourcesIntact()
    {
        // The dev shape: appsettings.Local.json supplies the key, the seam contributes nothing
        // and overwrites nothing.
        var config = WithSeam(
                new ConfigurationBuilder().AddInMemoryCollection(
                    [new("FieldEncryption:LocalMasterKeyBase64", "from-local-json")]),
                [Env("PATH", "/usr/bin")],
                _ => throw new InvalidOperationException("no file must be read"))
            .Build();

        config["FieldEncryption:LocalMasterKeyBase64"].ShouldBe("from-local-json");
    }

    [Fact]
    public void Composition_AddEnvFileSecrets_RegistersExactlyOneSourceAtTheEnd()
    {
        // Pins the extension itself (the public entry point both composition roots call) and
        // that it appends rather than inserts.
        var builder = new ConfigurationBuilder().AddInMemoryCollection([]);

        builder.AddEnvFileSecrets();

        builder.Sources.Count.ShouldBe(2);
        builder.Sources[^1].ShouldBeOfType<EnvFileSecretsConfigurationSource>();
    }
}
