using System.Runtime.CompilerServices;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// ADR 0066 (#802) — delad test-master-nyckel för fält-krypteringen + en
/// <see cref="ModuleInitializerAttribute"/> som sätter den som process-env-var
/// FÖRE någon test-host bootar. Efter AWS-exiten är <c>FieldEncryption:Provider</c>
/// default <c>"Local"</c> och validatorn hård-failar på en tom master-nyckel i
/// <b>ALLA</b> miljöer via <c>.ValidateOnStart()</c>; varje
/// <c>WebApplicationFactory&lt;Program&gt;</c>-host som bootar Api:t
/// (<see cref="ApiFactory"/> plus de fristående Configuration-/RateLimiting-/
/// nested-fabrikerna) måste därför ha en giltig 32-byte master-nyckel.
///
/// <para>
/// Den här module-init garanterar det <b>systemiskt</b> — en enda punkt, immun mot
/// att en ny standalone-host glömmer nyckeln (annars "grön lokalt, röd i CI":
/// lokalt bär en dev:s gitignored <c>appsettings.Local.json</c> nyckeln, i CI
/// finns ingen). Env-var-lagret läses av Program.cs
/// <c>WebApplication.CreateBuilder()</c> vid DI-tid; i CI är det enda källan.
/// <see cref="ApiFactory"/> sätter dessutom nyckeln via
/// <c>ConfigureAppConfiguration</c> (in-memory-last ⇒ vinner även över en dev:s
/// stale Local.json). Båda ger en giltig 32-byte-nyckel; round-trip kräver bara
/// SAMMA nyckel inom host-livstiden, inte en specifik.
/// </para>
/// </summary>
internal static class TestSecrets
{
    internal const string MasterKeyEnvVar = "FieldEncryption__LocalMasterKeyBase64";

    /// <summary>
    /// #842 — the Art. 17 audit pepper. <c>AuditPseudonymizationOptionsValidator</c> hard-fails on
    /// a missing/short pepper in <b>every</b> environment via <c>.ValidateOnStart()</c>, exactly
    /// like the field-encryption master key above, and for the same reason: an HMAC under an absent
    /// key looks protected while being trivially reversible. So every host that boots the Api needs
    /// one, and it is set here for the same systemic reason the master key is — a new standalone
    /// host cannot forget it, and CI (which has no dev's gitignored appsettings.Local.json) gets it
    /// from the same single point.
    /// </summary>
    internal const string AuditPepperEnvVar = "AuditPseudonymization__PepperBase64";

    // Deterministisk 32-byte AES-256 test-nyckel (0..31). Runtime-genererad, ingen
    // literal → gitleaks ser ingen hemlighet; det är test-nyckelmaterial, inte en
    // prod-secret (prod-master-nyckelns skydd är TD-102, self-managed på Hetzner).
    internal static readonly string MasterKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    // Deterministisk 32-byte test-pepper (100..131) — distinct from the master key so a test can
    // never pass by accidentally peppering with the encryption key. Runtime-generated, no literal.
    internal static readonly string AuditPepperBase64 =
        Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());

    [ModuleInitializer]
    internal static void SetDefaultMasterKey()
    {
        Environment.SetEnvironmentVariable(MasterKeyEnvVar, MasterKeyBase64);
        Environment.SetEnvironmentVariable(AuditPepperEnvVar, AuditPepperBase64);
    }
}
