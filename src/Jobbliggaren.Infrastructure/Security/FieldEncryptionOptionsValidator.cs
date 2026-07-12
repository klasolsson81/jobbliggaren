using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// ADR 0049 / ADR 0066 — startup-validering av <see cref="FieldEncryptionOptions"/>.
/// Efter AWS-exiten (#802) är <see cref="LocalDataKeyProvider"/> den enda
/// DEK-providern; den tidigare miljö-villkorade KMS-grenen (lenient i
/// Development/Test) är borttagen. Kvar är master-nyckel-guarden, som till
/// skillnad från den gamla CmkKeyId-guarden hård-failar i <b>ALLA</b> miljöer:
/// en tom/ogiltig/icke-32-byte lokal master-nyckel får aldrig tyst släppas
/// igenom (vore värre än ingen kryptering — det SER krypterat ut). Detta är
/// den kanoniska .NET-formen (<c>IValidateOptions</c> + <c>.ValidateOnStart()</c>)
/// för options-validering; den kör provider-agnostiskt (ingen
/// <c>IHostEnvironment</c>-gren längre).
/// </summary>
internal sealed class FieldEncryptionOptionsValidator : IValidateOptions<FieldEncryptionOptions>
{
    // Master-nyckeln måste vara AES-256 (32 byte) — paritet med
    // AesGcmFieldEncryptor.EnsureAes256Dek / LocalDataKeyProvider. ADR 0066.
    private const int Aes256KeySizeBytes = 32;

    // Provider-axeln fail-stoppas redan i DI (AddPersistence kastar på ett
    // explicit icke-Local-värde). Här valideras bara nyckel-axeln för den enda
    // kvarvarande (Local) providern.
    public ValidateOptionsResult Validate(string? name, FieldEncryptionOptions options)
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
}
