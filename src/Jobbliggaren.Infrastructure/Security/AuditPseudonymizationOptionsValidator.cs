using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Startup validation for <see cref="AuditPseudonymizationOptions"/> (#842). Hard-fails in
/// <b>every</b> environment, exactly like <see cref="FieldEncryptionOptionsValidator"/>, and for
/// the same reason: a missing or weak pepper must never be silently tolerated, because the result
/// <i>looks</i> pseudonymised while being trivially reversible. A control that only appears to
/// work is the defect class this whole issue exists to close.
/// </summary>
internal sealed class AuditPseudonymizationOptionsValidator
    : IValidateOptions<AuditPseudonymizationOptions>
{
    // HMAC-SHA256's block size is 64 bytes; a key shorter than the 32-byte output buys no extra
    // strength and a short one buys much less. 32 bytes is the floor, matching the house AES-256
    // key length so there is one number to remember.
    private const int MinimumPepperBytes = 32;

    public ValidateOptionsResult Validate(string? name, AuditPseudonymizationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PepperBase64))
        {
            return ValidateOptionsResult.Fail(
                "AuditPseudonymization:PepperBase64 saknas — Art. 17-raderingens audit-spår kan "
                + "inte pseudonymiseras (ADR 0090 D5, #842). Generera en nyckel "
                + "(openssl rand -base64 32) och lägg den i appsettings.Local.json (gitignored).");
        }

        byte[] pepper;
        try
        {
            pepper = Convert.FromBase64String(options.PepperBase64);
        }
        catch (FormatException)
        {
            // Never the pepper bytes, never the base64, in an error message (CLAUDE.md §5).
            return ValidateOptionsResult.Fail(
                "AuditPseudonymization:PepperBase64 är inte giltig base64.");
        }

        if (pepper.Length < MinimumPepperBytes)
        {
            var actualLength = pepper.Length;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(pepper);
            return ValidateOptionsResult.Fail(
                $"AuditPseudonymization:PepperBase64 måste dekoda till minst "
                + $"{MinimumPepperBytes} byte, fick {actualLength} byte.");
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(pepper);
        return ValidateOptionsResult.Success;
    }
}
