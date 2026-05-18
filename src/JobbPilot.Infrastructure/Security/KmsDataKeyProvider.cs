using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using JobbPilot.Application.Common.Security;
using JobbPilot.Domain.JobSeekers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Security;

/// <summary>
/// TD-13 (ADR 0049 Beslut 1) — per-användare-DEK via AWS KMS envelope
/// encryption. <c>GenerateDataKey</c> skapar DEK; <c>Decrypt</c> unwrappar.
/// Encryption-context binder kryptografiskt wrapped-DEK till ägaren
/// (<see cref="JobSeekerId"/>) — en wrapped-DEK kan inte unwrappas i fel
/// användarkontext även om en rad kopieras (AWS KMS best practice,
/// discovery §5). Region/CMK-ARN via <see cref="FieldEncryptionOptions"/>
/// (Migrate-precedens, ADR 0049 Kontext).
///
/// Fail-closed: KMS-fel propageras — aldrig en default/tom/klartext-DEK
/// (CTO-domen 2026-05-18). §5.4: varken plaintext-DEK eller wrapped-bytes
/// loggas någonsin (endast ägar-id + operation + exception-typ).
/// </summary>
public sealed partial class KmsDataKeyProvider : IDataKeyProvider
{
    private readonly IAmazonKeyManagementService _kms;
    private readonly ILogger<KmsDataKeyProvider> _logger;
    private readonly FieldEncryptionOptions _options;

    // Test-kontrakt (TD-13 C1, test-writer gap #2): ctorn tar (kms, logger);
    // CMK-ARN binds via IOptions i produktion (valfri här — failure-testerna
    // mockar KMS att kasta innan CMK-id når någon assertion).
    public KmsDataKeyProvider(
        IAmazonKeyManagementService kms,
        ILogger<KmsDataKeyProvider> logger,
        IOptions<FieldEncryptionOptions>? options = null)
    {
        _kms = kms;
        _logger = logger;
        _options = options?.Value ?? new FieldEncryptionOptions();
    }

    public async Task<GeneratedDataKey> CreateDataKeyAsync(
        JobSeekerId owner, CancellationToken ct)
    {
        try
        {
            var response = await _kms.GenerateDataKeyAsync(
                new GenerateDataKeyRequest
                {
                    KeyId = _options.CmkKeyId,
                    KeySpec = DataKeySpec.AES_256,
                    EncryptionContext = EncryptionContext(owner),
                },
                ct);

            // CiphertextBlob = wrapped DEK (lagras); Plaintext = DEK (i minne).
            return new GeneratedDataKey(
                response.Plaintext.ToArray(),
                response.CiphertextBlob.ToArray(),
                response.KeyId);
        }
        catch (OperationCanceledException)
        {
            // Cancel är inte ett KMS-fel — propagera utan fel-logg.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed: ingen halvfärdig GeneratedDataKey. Inget
            // nyckelmaterial i loggen (§5.4) — bara ägar-id + exception-typ.
            LogGenerateFailed(owner.Value, ex.GetType().Name);
            throw;
        }
    }

    public async Task<byte[]> UnwrapDataKeyAsync(
        JobSeekerId owner, byte[] wrappedDek, CancellationToken ct)
    {
        try
        {
            using var blob = new MemoryStream(wrappedDek, writable: false);
            var response = await _kms.DecryptAsync(
                new DecryptRequest
                {
                    CiphertextBlob = blob,
                    EncryptionContext = EncryptionContext(owner),
                },
                ct);

            return response.Plaintext.ToArray();
        }
        catch (OperationCanceledException)
        {
            // Cancel är inte ett KMS-fel — propagera utan fel-logg.
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed: aldrig en fallback-DEK. §5.4 — wrapped-bytes loggas
            // ALDRIG (varken base64/hex/CSV); bara ägar-id + exception-typ.
            LogUnwrapFailed(owner.Value, ex.GetType().Name);
            throw;
        }
    }

    // Identiskt på GenerateDataKey + Decrypt (AAD måste matcha).
    private static Dictionary<string, string> EncryptionContext(JobSeekerId owner) =>
        new()
        {
            ["aggregate"] = "jobseeker",
            ["owner"] = owner.Value.ToString(),
            ["purpose"] = "td13-field",
        };

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "KMS GenerateDataKey misslyckades för JobSeeker {Owner} ({ExceptionType}).")]
    private partial void LogGenerateFailed(Guid owner, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "KMS Decrypt (unwrap DEK) misslyckades för JobSeeker {Owner} ({ExceptionType}).")]
    private partial void LogUnwrapFailed(Guid owner, string exceptionType);
}
