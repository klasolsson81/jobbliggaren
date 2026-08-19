using Jobbliggaren.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1350 — DI-gate for <see cref="DependencyInjection.AddApiDataProtection"/>.
///
/// <para>
/// Nothing called <c>AddDataProtection</c> before that method existed, so the keyring sat at the
/// framework default inside the container's writable layer: every recreate minted a fresh one and
/// silently invalidated every outstanding activation, password-reset and change-email link. The
/// framework logs both halves of that at every boot and nobody had read the lines.
/// </para>
///
/// <para>
/// Registration inspection only — no host boot, no Testcontainers. That is why the registration is
/// its own method rather than four lines inside <c>AddIdentityAndSessions</c>, which cannot be
/// called without Postgres and Redis (CLAUDE.md §2.4).
/// </para>
///
/// <para>
/// The deployed side of the same contract — that the box actually sets the key and mounts a
/// WRITABLE volume at the path it names — is pinned by <c>DeployComposeDataProtectionTests</c> in
/// <c>Jobbliggaren.Migrate.UnitTests</c>, which is the project the compose file is copied into.
/// Neither pin means much alone: this one proves the code honours the key, that one proves the
/// deployment supplies it, and the config-key constant is what joins them.
/// </para>
/// </summary>
public class AddApiDataProtectionGateTests
{
    private static ServiceProvider Build(string? keyPath)
    {
        var values = new Dictionary<string, string?>();
        if (keyPath is not null)
            values[DependencyInjection.DataProtectionKeyPathConfigKey] = keyPath;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddApiDataProtection_WithAKeyPath_PersistsTheKeyringThere()
    {
        var path = Path.Combine(Path.GetTempPath(), "jbl-dp-gate-test");

        using var provider = Build(path);

        var repository = provider
            .GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        repository.ShouldBeOfType<FileSystemXmlRepository>(
            customMessage:
            "Without a file-system repository the keyring lives wherever the framework defaults to " +
            "— in the container that is the writable layer, which does not survive a recreate. " +
            "Every outstanding activation and password-reset link dies on the next deploy, and the " +
            "user sees a response indistinguishable from an expired token.")
            .Directory.FullName.ShouldBe(new DirectoryInfo(path).FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddApiDataProtection_WithoutAKeyPath_LeavesTheFrameworkDefault(string? keyPath)
    {
        using var provider = Build(keyPath);

        // Deliberately optional, following the ratified Seq:ServerUrl shape: a fresh dev boot sets
        // nothing and is unchanged, so this adds no fail-fast key and no CLAUDE.md §11 dev-boot
        // obligation. Whitespace counts as unset — a half-filled .env must not point the keyring at
        // a directory named " ".
        provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository
            .ShouldBeNull();
    }

    [Fact]
    public void AddApiDataProtection_PinsTheApplicationDiscriminator()
    {
        using var provider = Build(null);

        // Without this the discriminator is derived from IHostEnvironment.ContentRootPath, so an
        // image-layout change would silently stop the PERSISTED keyring resolving older tokens —
        // the same defect as before the fix, and harder to see because the keys are still there.
        provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator
            .ShouldBe(DependencyInjection.DataProtectionApplicationName);
    }

    [Fact]
    public void DataProtectionKeyPathConfigKey_MatchesTheEnvironmentVariableTheDeployStackSets()
    {
        // The joining fact. The compose file writes `DataProtection__KeyPath`, and ASP.NET Core's
        // environment-variable provider maps `__` to `:`. If this constant is renamed and the
        // compose file is not, the box boots with an unpersisted keyring and nothing says so —
        // which is the original defect, restored silently. DeployComposeDataProtectionTests asserts
        // the other end of this same string.
        DependencyInjection.DataProtectionKeyPathConfigKey.Replace(":", "__")
            .ShouldBe("DataProtection__KeyPath");
    }
}
