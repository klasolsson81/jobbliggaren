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

    // Deterministisk 32-byte AES-256 test-nyckel (0..31). Runtime-genererad, ingen
    // literal → gitleaks ser ingen hemlighet; det är test-nyckelmaterial, inte en
    // prod-secret (prod-master-nyckelns skydd är TD-102, self-managed på Hetzner).
    internal static readonly string MasterKeyBase64 =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    [ModuleInitializer]
    internal static void SetDefaultMasterKey() =>
        Environment.SetEnvironmentVariable(MasterKeyEnvVar, MasterKeyBase64);
}
