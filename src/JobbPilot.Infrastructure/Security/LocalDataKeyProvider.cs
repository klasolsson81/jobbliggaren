using System.Security.Cryptography;
using System.Text;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.JobSeekers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// ADR 0066 (AWS-avveckling) — lokal/Hetzner-ersättare för
/// <see cref="KmsDataKeyProvider"/>. Implementerar samma envelope-kontrakt
/// (<see cref="IDataKeyProvider"/>) men wrappar per-användar-DEK:en med en
/// lokal AES-256-GCM master-nyckel istället för AWS KMS. Vald via
/// <c>FieldEncryption:Provider="Local"</c> (DI-switch i AddPersistence).
///
/// <para>
/// <b>Envelope bevarad:</b> per-JobSeeker genereras en 32-byte DEK
/// (<see cref="RandomNumberGenerator"/>); DEK:en wrappas med master-nyckeln och
/// lagras i <c>user_data_keys</c> precis som KMS-varianten. Fält-krypteringen
/// (<see cref="IFieldEncryptor"/>/<see cref="KmsEnvelopeEncryptor"/>) är
/// oförändrad — bara DEK-wrap byter mekanism. Detta är inte bara en dev-hack:
/// samma mönster bär Hetzner-prod-kryptering utan KMS (master-nyckel injicerad
/// via env/secret).
/// </para>
///
/// <para>
/// <b>Owner-binding (AAD):</b> <see cref="JobSeekerId"/> binds som Associated
/// Data i AES-GCM-wrapen (motsvarar KMS <c>EncryptionContext</c>) — en wrapped-
/// DEK kan inte unwrappas i fel ägarkontext även om en rad kopieras.
/// </para>
///
/// <para>
/// <b>Wire-format (wrapped-DEK):</b>
/// <c>[0x4C, 0x01] || nonce(12) || ciphertext(32) || tag(16)</c>.
/// Prefix <c>0x4C</c> ('L' = Local), <c>0x01</c> = wrap-format-version 1 —
/// crypto-agility: en framtida master-nyckel-rotation (v2) failar tydligt vid
/// unwrap istället för som auth-tag-mismatch.
/// </para>
///
/// <para>
/// <b>Fail-closed (CTO-domen 2026-05-18, bevarad):</b> krypto-/format-/auth-fel
/// propageras som <see cref="CryptographicException"/> — aldrig en
/// default/tom/klartext-DEK. Varken master-nyckel, DEK eller wrapped-bytes
/// loggas någonsin (§5.4) — endast ägar-id + operation + exception-typ.
/// </para>
/// </summary>
public sealed partial class LocalDataKeyProvider : IDataKeyProvider
{
    private const int Aes256KeySize = 32;  // DEK + master-nyckel = AES-256
    private const int NonceSize = 12;      // AES-GCM standard-nonce
    private const int TagSize = 16;        // AES-GCM auth-tag (128-bit)

    private const byte WrapMagic = 0x4C;       // 'L' = Local
    private const byte WrapFormatVersion = 0x01;

    // CmkKeyId-motsvarighet — rent metadata/forensik-fält i user_data_keys.
    // Ingen läsväg jämför detta för krypto-beslut (UnwrapDataKeyAsync tar bara
    // owner + wrappedDek). varchar(2048) rymmer det.
    private const string LocalKeyId = "local-v1";

    private readonly byte[] _masterKey;
    private readonly ILogger<LocalDataKeyProvider> _logger;

    public LocalDataKeyProvider(
        IOptions<FieldEncryptionOptions> options,
        ILogger<LocalDataKeyProvider> logger)
    {
        _logger = logger;

        // Re-guard förbi options-pipelinen (paritet KmsEnvelopeEncryptor.
        // EnsureAes256Dek): master-nyckeln MÅSTE vara 32 byte. Fel-längd/format
        // → fail-closed redan vid konstruktion. Ingen nyckel-byte i message.
        var base64 = options.Value.LocalMasterKeyBase64;
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new CryptographicException(
                "FieldEncryption:LocalMasterKeyBase64 saknas — lokal envelope " +
                "kan inte initieras (ADR 0066).");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new CryptographicException(
                "FieldEncryption:LocalMasterKeyBase64 är inte giltig base64.");
        }

        if (key.Length != Aes256KeySize)
        {
            var actualLength = key.Length;
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException(
                $"FieldEncryption:LocalMasterKeyBase64 måste dekoda till " +
                $"{Aes256KeySize} byte (AES-256), fick {actualLength} byte.");
        }

        // Master-nyckeln lever singleton-instansens livstid (paritet med KMS-
        // klientens livstid). Exponeras aldrig, kopieras aldrig ut.
        _masterKey = key;
    }

    public Task<GeneratedDataKey> CreateDataKeyAsync(
        JobSeekerId owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dek = RandomNumberGenerator.GetBytes(Aes256KeySize);
        try
        {
            var wrapped = Wrap(dek, owner);
            // Plaintext-DEK ägs nu av anroparen (UserDataKeyStore nollar via cache);
            // wrapped-DEK lagras. Returnera utan att nolla dek (kontraktet säger
            // anroparen nollar PlaintextDek efter bruk).
            return Task.FromResult(new GeneratedDataKey(dek, wrapped, LocalKeyId));
        }
        catch (Exception ex)
        {
            // Fail-closed: ingen halvfärdig GeneratedDataKey. Nolla DEK:en —
            // den når aldrig anroparen. Inget nyckelmaterial i loggen (§5.4).
            CryptographicOperations.ZeroMemory(dek);
            LogWrapFailed(owner.Value, ex.GetType().Name);
            throw;
        }
    }

    public Task<byte[]> UnwrapDataKeyAsync(
        JobSeekerId owner, byte[] wrappedDek, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(Unwrap(wrappedDek, owner));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed: aldrig en fallback-DEK. Wrapped-bytes loggas ALDRIG
            // (§5.4) — bara ägar-id + exception-typ.
            LogUnwrapFailed(owner.Value, ex.GetType().Name);
            throw;
        }
    }

    private byte[] Wrap(ReadOnlySpan<byte> dek, JobSeekerId owner)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[dek.Length];
        var tag = new byte[TagSize];
        var aad = BuildAad(owner);

        using (var aes = new AesGcm(_masterKey, TagSize))
        {
            aes.Encrypt(nonce, dek, ciphertext, tag, aad);
        }

        // [magic, version] || nonce || ciphertext || tag
        var payload = new byte[2 + NonceSize + ciphertext.Length + TagSize];
        payload[0] = WrapMagic;
        payload[1] = WrapFormatVersion;
        nonce.CopyTo(payload.AsSpan(2));
        ciphertext.CopyTo(payload.AsSpan(2 + NonceSize));
        tag.CopyTo(payload.AsSpan(2 + NonceSize + ciphertext.Length));
        return payload;
    }

    private byte[] Unwrap(byte[] wrappedDek, JobSeekerId owner)
    {
        if (wrappedDek is null || wrappedDek.Length < 2 + NonceSize + TagSize)
        {
            throw new CryptographicException("Wrapped-DEK är för kort eller saknas.");
        }

        // Crypto-agility-guard: endast v1-layouten är känd. Okänt prefix failar
        // tydligt istället för som auth-tag-mismatch.
        if (wrappedDek[0] != WrapMagic || wrappedDek[1] != WrapFormatVersion)
        {
            throw new CryptographicException(
                "Okänt wrap-format för lokal envelope — endast v1 stöds.");
        }

        var cipherLength = wrappedDek.Length - 2 - NonceSize - TagSize;
        var nonce = wrappedDek.AsSpan(2, NonceSize);
        var ciphertext = wrappedDek.AsSpan(2 + NonceSize, cipherLength);
        var tag = wrappedDek.AsSpan(2 + NonceSize + cipherLength, TagSize);
        var dek = new byte[cipherLength];
        var aad = BuildAad(owner);

        try
        {
            using var aes = new AesGcm(_masterKey, TagSize);
            // Kastar AuthenticationTagMismatchException (CryptographicException)
            // vid fel owner-AAD / manipulering / fel master-nyckel — bubblar,
            // ingen DEK returneras.
            aes.Decrypt(nonce, ciphertext, tag, dek, aad);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new CryptographicException(
                "Unwrap av lokal DEK misslyckades (auth-tag-fel, fel ägare " +
                "eller fel master-nyckel).");
        }

        return dek;
    }

    // Deterministisk AAD = samma owner-binding som KMS EncryptionContext
    // (aggregate/owner/purpose). Hårdkodad nyckel-ordning + Guid :D-format
    // (gemener, bindestreck, kulturoberoende) + UTF-8 → wrap och unwrap
    // producerar identiska AAD-bytes för samma ägare.
    private static byte[] BuildAad(JobSeekerId owner) =>
        Encoding.UTF8.GetBytes(
            $"aggregate=jobseeker;owner={owner.Value:D};purpose=td13-field");

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Lokal DEK-wrap misslyckades för JobSeeker {Owner} ({ExceptionType}).")]
    private partial void LogWrapFailed(Guid owner, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Lokal DEK-unwrap misslyckades för JobSeeker {Owner} ({ExceptionType}).")]
    private partial void LogUnwrapFailed(Guid owner, string exceptionType);
}
