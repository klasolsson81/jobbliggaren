using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

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
    // Wrappad-DEK-master-nyckeln måste vara AES-256 (32 byte) — paritet med
    // KmsEnvelopeEncryptor.EnsureAes256Dek. ADR 0066 (lokal envelope).
    private const int Aes256KeySizeBytes = 32;

    public ValidateOptionsResult Validate(string? name, FieldEncryptionOptions options)
    {
        // Provider-axeln avgör vilka guards som gäller (ADR 0066). Kms-grenen
        // behåller CmkKeyId + EU-region-guard; Local-grenen validerar master-
        // nyckeln. Okänd provider fail-stoppas redan i DI (AddPersistence) —
        // här valideras bara de två kända grenarna defensivt.
        if (string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateLocal(options);
        }

        return ValidateKms(options);
    }

    private ValidateOptionsResult ValidateKms(FieldEncryptionOptions options)
    {
        var isProdOrStaging = environment.IsProduction() || environment.IsStaging();

        // EU-residens-guard (security-auditor C3 Medium 1, ADR 0049 / Area 5
        // cross-region): PII-DEK:er får aldrig wrappas/unwrappas utanför EU.
        // En felkonfigurerad icke-eu-region (t.ex. us-east-1 via env-override)
        // måste hård-faila i Production/Staging — paritet med CmkKeyId-guarden.
        if (!string.IsNullOrWhiteSpace(options.AwsRegion)
            && !options.AwsRegion.StartsWith("eu-", StringComparison.OrdinalIgnoreCase)
            && isProdOrStaging)
        {
            return ValidateOptionsResult.Fail(
                $"FieldEncryption:AwsRegion '{options.AwsRegion}' är inte en " +
                "EU-region — PII-DEK:er måste stanna i EU (ADR 0049). " +
                "Obligatoriskt EU i Production/Staging.");
        }

        if (!string.IsNullOrWhiteSpace(options.CmkKeyId))
        {
            return ValidateOptionsResult.Success;
        }

        if (isProdOrStaging)
        {
            return ValidateOptionsResult.Fail(
                "FieldEncryption:CmkKeyId saknas — KMS-envelope kan inte " +
                "initieras (ADR 0049). Obligatorisk i Production/Staging.");
        }

        LogMissingCmkInNonProduction(environment.EnvironmentName);
        return ValidateOptionsResult.Success;
    }

    // Local-grenen (ADR 0066): master-nyckeln är det enda fail-closed-skyddet
    // (ingen KMS att falla tillbaka på). Till skillnad från CmkKeyId hård-failar
    // en tom/ogiltig/icke-32-byte master-nyckel i ALLA miljöer — en degraderad
    // lokal nyckel får aldrig tyst släppas igenom (vore värre än ingen kryptering:
    // det SER krypterat ut). EU-residens är här en infra-nivå-egenskap (Hetzner-EU),
    // inte en options-egenskap — AwsRegion-guarden är medvetet no-op i Local.
    private static ValidateOptionsResult ValidateLocal(FieldEncryptionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LocalMasterKeyBase64))
        {
            return ValidateOptionsResult.Fail(
                "FieldEncryption:LocalMasterKeyBase64 saknas — lokal envelope " +
                "kan inte initieras (ADR 0066). Generera en 32-byte nyckel och " +
                "lägg i appsettings.Local.json (gitignored).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.LocalMasterKeyBase64);
        }
        catch (FormatException)
        {
            // Aldrig nyckel-bytes/base64 i fel-meddelandet (§5.4).
            return ValidateOptionsResult.Fail(
                "FieldEncryption:LocalMasterKeyBase64 är inte giltig base64.");
        }

        if (key.Length != Aes256KeySizeBytes)
        {
            var actualLength = key.Length;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
            return ValidateOptionsResult.Fail(
                $"FieldEncryption:LocalMasterKeyBase64 måste dekoda till " +
                $"{Aes256KeySizeBytes} byte (AES-256), fick {actualLength} byte.");
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
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
