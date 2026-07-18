using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Security;

/// <summary>
/// Startup validation for <see cref="CompanyWatchPseudonymizationOptions"/> (#544). Hard-fails in
/// <b>every</b> environment, mirroring <see cref="AuditPseudonymizationOptionsValidator"/> 1:1
/// (security-auditor B1) and for the same reason: a missing or weak pepper must never be silently
/// tolerated, because a personnummer-shaped org.nr tokenised under a weak/absent key <i>looks</i>
/// protected while being trivially brute-force-reversible (the pnr space is small).
/// </summary>
internal sealed class CompanyWatchPseudonymizationOptionsValidator
    : IValidateOptions<CompanyWatchPseudonymizationOptions>
{
    // 32 bytes is the floor — matching the audit pepper and the house AES-256 key length, so there
    // is one number to remember. A key shorter than HMAC-SHA256's 32-byte output buys no strength.
    private const int MinimumPepperBytes = 32;

    public ValidateOptionsResult Validate(string? name, CompanyWatchPseudonymizationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PepperBase64))
        {
            return ValidateOptionsResult.Fail(
                "CompanyWatchPseudonymization:PepperBase64 saknas — en enskild-firmas "
                + "organisationsnummer (= personnummer) kan inte tokeniseras at-rest (ADR 0090 D5, "
                + "#544). Generera en nyckel (openssl rand -base64 32) och lägg den i "
                + "appsettings.Local.json (gitignored).");
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
                "CompanyWatchPseudonymization:PepperBase64 är inte giltig base64.");
        }

        if (pepper.Length < MinimumPepperBytes)
        {
            var actualLength = pepper.Length;
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(pepper);
            return ValidateOptionsResult.Fail(
                $"CompanyWatchPseudonymization:PepperBase64 måste dekoda till minst "
                + $"{MinimumPepperBytes} byte, fick {actualLength} byte.");
        }

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(pepper);
        return ValidateOptionsResult.Success;
    }
}
