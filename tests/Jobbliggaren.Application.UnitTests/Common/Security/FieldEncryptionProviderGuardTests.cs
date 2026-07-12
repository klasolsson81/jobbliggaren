using Jobbliggaren.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// #802 / ADR 0066 — DI fail-fast-guarden: AddPersistence måste dö loud på ett
/// explicit icke-Local <c>FieldEncryption:Provider</c> (en kvarlämnad <c>"Kms"</c>
/// i stale config), aldrig tyst falla till Local — det är hela footgun-klassen
/// #802 dödar. Guarden kastar ivrigt vid registrering (config-läsning före
/// DB-anslutning) → rent unit-test, inget Postgres. Utan detta test skulle en
/// framtida "förenkling" till en ovillkorlig LocalDataKeyProvider-registrering
/// tyst återinföra footgunen och ändå passera alla andra tester.
/// </summary>
public class FieldEncryptionProviderGuardTests
{
    private static IConfiguration Config(string? provider)
    {
        var dict = new Dictionary<string, string?>
        {
            // Välformad men aldrig ansluten (UseNpgsql registrerar bara, ansluter ej).
            ["ConnectionStrings:Postgres"] =
                "Host=localhost;Database=test;Username=test;Password=test",
            ["FieldEncryption:LocalMasterKeyBase64"] =
                Convert.ToBase64String(new byte[32]),
        };
        if (provider is not null)
        {
            dict["FieldEncryption:Provider"] = provider;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Theory]
    [InlineData("Kms")]
    [InlineData("kms")]      // case-insensitiv guard
    [InlineData("Aws")]
    public void AddPersistence_WithStaleNonLocalProvider_ThrowsAtRegistration(string provider)
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddPersistence(Config(provider)));

        // Pinna att det är GUARDEN som kastar (inte en orelaterad
        // InvalidOperationException längre ned i AddPersistence).
        ex.Message.ShouldContain("FieldEncryption:Provider");
    }

    [Fact]
    public void AddPersistence_WithLocalProvider_DoesNotThrowOnGuard()
    {
        Should.NotThrow(() => new ServiceCollection().AddPersistence(Config("Local")));
    }

    [Fact]
    public void AddPersistence_WithOmittedProvider_DoesNotThrowOnGuard()
    {
        // Utelämnad Provider defaultar Local → ingen fail-fast.
        Should.NotThrow(() => new ServiceCollection().AddPersistence(Config(null)));
    }
}
