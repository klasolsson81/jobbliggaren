using System.Security.Cryptography;
using Jobbliggaren.Infrastructure.Security;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// CvReviewFingerprintPseudonymizationOptionsValidator (#692, ADR 0093 §D2(e)) — the fail-closed
/// startup guard for the SEPARATE CV-review finding-fingerprint pepper. Mirrors
/// <see cref="CompanyWatchPseudonymizationOptionsValidatorTests"/> 1:1 (security-auditor B1).
/// </summary>
/// <remarks>
/// <b>This validator is the whole defence, and it needs a test.</b> <see cref="HmacFindingFingerprinter"/>
/// cannot defend itself: HMAC-SHA256 accepts a zero-length key and happily returns a digest, so an
/// absent/weak pepper yields an effectively UNKEYED fingerprint of short CV quotes at rest in
/// <c>resume_finding_statuses</c> — dictionary/brute-forceable if the ledger leaks, and it would
/// <i>look</i> keyed while protecting nothing. Nothing else stands in the way: this validator, plus
/// <c>ValidateOnStart()</c> in <c>AddCvReview</c>. Hence the boundary cases below, not just the happy path.
/// </remarks>
public class CvReviewFingerprintPseudonymizationOptionsValidatorTests
{
    private static CvReviewFingerprintPseudonymizationOptionsValidator Validator() => new();

    private static string PepperOf(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string pepper) =>
        Validator().Validate(null, new CvReviewFingerprintPseudonymizationOptions { PepperBase64 = pepper });

    [Fact]
    public void A_valid_32_byte_pepper_succeeds()
    {
        Validate(PepperOf(32)).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The floor, at the boundary. 32 bytes matches HMAC-SHA256's output length and the house AES-256
    /// key length — one number to remember, shared with the audit and watch peppers.
    /// </summary>
    [Fact]
    public void Exactly_32_bytes_is_accepted_and_31_is_not()
    {
        Validate(Convert.ToBase64String(new byte[32])).Succeeded.ShouldBeTrue();
        Validate(Convert.ToBase64String(new byte[31])).Failed.ShouldBeTrue(
            "a short pepper buys much less strength while looking identical from the outside.");
    }

    /// <summary>
    /// <b>The case that matters most.</b> No pepper configured ⇒ boot must abort. If this rule ever
    /// goes soft, every finding fingerprint in <c>resume_finding_statuses</c> silently becomes an
    /// unkeyed, brute-force-reversible digest of a short CV quote.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_pepper_fails_in_every_environment(string pepper)
    {
        var result = Validate(pepper);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("PepperBase64");
    }

    [Fact]
    public void A_malformed_base64_pepper_fails()
    {
        Validate("inte!giltig!base64!").Failed.ShouldBeTrue();
    }

    /// <summary>
    /// The pepper is key material. It must never appear in the failure message that a startup abort
    /// writes to a log sink (CLAUDE.md §5) — the guard must not leak the thing it guards.
    /// </summary>
    [Fact]
    public void A_failure_message_never_echoes_the_pepper_material()
    {
        var shortPepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var result = Validate(shortPepper);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotContain(shortPepper, Case.Sensitive,
            "a guard that logs the secret it rejected has leaked it to every log sink.");
    }
}
