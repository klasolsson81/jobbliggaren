using Jobbliggaren.Infrastructure.Configuration;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// #198 / ADR 0050 gate B-1 — the <c>&lt;KEY&gt;_FILE</c> configuration source that lets the
/// master key and the three peppers reach Api/Worker as FILES rather than as container
/// environment values (Docker persists environment to disk in its own container state, and
/// <c>docker inspect</c> returned the key after the container had exited — measured on the box
/// 2026-08-05, #1240).
///
/// <para>
/// Resolution is a pure function taking the environment as data and the file read as a
/// delegate, so these cases need neither process environment nor filesystem — the same shape
/// <c>MigrateEnvTests</c> uses against <c>MigrateEnv.Resolve</c>.
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
        // A secret mount or a shell redirect commonly leaves a trailing newline, and for a
        // pepper one stray byte changes every derived HMAC.
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
        // Contributing an empty string here would produce a second, competing message.
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
            // A real provider can echo file content into an IO exception message; the source
            // must not propagate that.
            _ => throw new IOException($"device error while reading {secret}")));

        ex.Message.ShouldContain(path);
        ex.Message.ShouldContain("FieldEncryption__LocalMasterKeyBase64_FILE");
        // CLAUDE.md §5: never secret material in an exception message.
        ex.Message.ShouldNotContain(secret);
    }

    [Fact]
    public void BlankPointer_IsNotConfigured_RatherThanAnError()
    {
        // Lets a compose file carry the variable while an environment that does not use files
        // leaves it empty.
        var data = Resolve(
            [Env("FieldEncryption__LocalMasterKeyBase64_FILE", "   ")],
            _ => throw new InvalidOperationException("no file must be read"));

        data.ShouldBeEmpty();
    }

    [Fact]
    public void BareSuffixVariable_IsIgnored()
    {
        // "_FILE" alone would otherwise resolve to an empty configuration key.
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
}
