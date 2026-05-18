using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 4 — mekanik-not 2026-05-18 STOPP I, CTO-triage
/// Approach D). Miljö-villkorad startup-validering av
/// <see cref="FieldEncryptionOptions"/>.
///
/// <para>
/// ADR 0049:s ordalydelse "tom CmkKeyId ska validera bort vid startup
/// (.ValidateOnStart())" var en <b>implementeringsförväntan om mekanism</b>,
/// inte besluts-substans. Substansen är: fält-PII får aldrig
/// krypteras/dekrypteras mot saknad/ogiltig CMK (fail-closed) — den
/// invarianten bevaras oförändrad av <c>KmsDataKeyProvider</c>:s
/// runtime-guard (KMS avvisar tom KeyId, ingen klartext-fallback) i ALLA
/// miljöer. En global <c>.Validate(Func)</c> ser per .NET-design inte
/// <see cref="IHostEnvironment"/> och applicerade därför en Production-
/// invariant på Test-hostar (~6 WebApplicationFactory som medvetet fakar
/// KMS) → J3-broken main. Detta är den kanoniska .NET-formen
/// (<c>IValidateOptions</c> med konstruktorberoende) för miljö-medveten
/// options-validering.
/// </para>
///
/// Hård fail-fast i Production/Staging (där KMS måste fungera — tom CMK är
/// ett deploy-fel som ska döda hosten omedelbart). I Development/Test:
/// strukturerad warning, blockerar ej boot (fail-closed kvarstår via
/// runtime-guarden — boot-checken var alltid redundant defense-in-depth,
/// meningsfull endast där KMS måste fungera).
/// </summary>
internal sealed partial class FieldEncryptionOptionsValidator(
    IHostEnvironment environment,
    ILogger<FieldEncryptionOptionsValidator> logger)
    : IValidateOptions<FieldEncryptionOptions>
{
    public ValidateOptionsResult Validate(string? name, FieldEncryptionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CmkKeyId))
        {
            return ValidateOptionsResult.Success;
        }

        if (environment.IsProduction() || environment.IsStaging())
        {
            return ValidateOptionsResult.Fail(
                "FieldEncryption:CmkKeyId saknas — KMS-envelope kan inte " +
                "initieras (ADR 0049). Obligatorisk i Production/Staging.");
        }

        LogMissingCmkInNonProduction(environment.EnvironmentName);
        return ValidateOptionsResult.Success;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "FieldEncryption:CmkKeyId saknas i miljö {Environment} — " +
                  "boot tillåts (Development/Test). Fält-kryptering är fortsatt " +
                  "fail-closed: KMS avvisar tom CMK vid första krypto-operation " +
                  "(ADR 0049 Beslut 4 mekanik-not).")]
    private partial void LogMissingCmkInNonProduction(string environment);
}
