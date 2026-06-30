using Jobbliggaren.Domain.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — invariants for the <see cref="OrganizationNumber"/> value object:
/// the 10-digit format guard and the personnummer-shape detector (D8(c) surfacing guard).
/// </summary>
public class OrganizationNumberTests
{
    // ---------------------------------------------------------------
    // Create — format guard
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("5592804784")] // live-verified legal-entity org.nr (third digit 9)
    [InlineData("2120000142")] // third digit 2 — legal entity
    public void Create_WithTenDigits_Succeeds(string value)
    {
        var result = OrganizationNumber.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
        result.Value.ToString().ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrBlank_FailsRequired(string? value)
    {
        var result = OrganizationNumber.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OrganizationNumber.Required");
    }

    [Theory]
    [InlineData("559280478")]    // 9 digits
    [InlineData("55928047840")]  // 11 digits
    [InlineData("559280478X")]   // non-digit
    [InlineData("559280-4784")]  // hyphen (we store verbatim, no normalisation)
    [InlineData("5592804784\n")] // trailing newline — \z (not $) rejects it
    public void Create_WithNonTenDigits_FailsInvalid(string value)
    {
        var result = OrganizationNumber.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OrganizationNumber.Invalid");
    }

    // ---------------------------------------------------------------
    // IsPersonnummerShaped — D8(c) surfacing guard (conservative, non-primary)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("2120000142")] // third digit 2
    [InlineData("5592804784")] // third digit 9
    [InlineData("9696000003")] // third digit 9
    public void IsPersonnummerShaped_ForLegalEntityOrgNumber_IsFalse(string value)
    {
        // A Swedish legal-entity org.nr always has third digit ≥ 2 → not personnummer-shaped.
        OrganizationNumber.Create(value).Value.IsPersonnummerShaped().ShouldBeFalse();
    }

    [Theory]
    [InlineData("9001011234")] // YYMMDD 900101 → third digit 0 (month 01)
    [InlineData("8512319876")] // YYMMDD 851231 → third digit 1 (month 12)
    [InlineData("0010100000")] // third digit 1
    public void IsPersonnummerShaped_ForSoleProprietorPersonnummerForm_IsTrue(string value)
    {
        // A personnummer's third digit is the month tens-digit (0 for 01–09, 1 for 10–12) → < 2.
        // A sole-prop (enskild firma) org.nr equals the owner's personnummer → MUST be flagged.
        OrganizationNumber.Create(value).Value.IsPersonnummerShaped().ShouldBeTrue();
    }

    [Theory]
    [InlineData("abc")]          // not 10 digits
    [InlineData("123456789X")]   // 10 chars but a non-digit
    public void IsPersonnummerShaped_ForMalformedTrustedValue_FailsSafeToTrue(string trusted)
    {
        // Fail-safe default (D8(c)): a value that is not exactly 10 digits is treated as sensitive.
        // Unreachable on a validly-constructed instance — exercised here via FromTrusted to pin the
        // safe-default-sensitive branch (it must NEVER under-flag).
        OrganizationNumber.FromTrusted(trusted).IsPersonnummerShaped().ShouldBeTrue();
    }
}
