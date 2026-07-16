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

    [Theory]
    // digitZero = the script's decimal '0' codepoint; the look-alike of 5592804784 is built by
    // codepoint arithmetic (a literal exotic glyph corrupts across tooling — house lesson).
    // FF10 = FULLWIDTH ZERO, 0660 = ARABIC-INDIC ZERO. Both satisfy \d (\p{Nd}) yet are not
    // ASCII [0-9] — before #865 they PASSED, got stored plaintext, and could never equality-
    // match the ASCII register/job_ads values: a watch that silently matches nothing forever.
    [InlineData(0xFF10)]
    [InlineData(0x0660)]
    public void Create_WithUnicodeDigitLookalike_FailsInvalid_NotSilentlyAccepted(int digitZero)
    {
        var lookalike = new string("5592804784"
            .Select(c => (char)(digitZero + (c - '0')))
            .ToArray());

        var result = OrganizationNumber.Create(lookalike);

        result.IsFailure.ShouldBeTrue(
            $"'{lookalike}' (U+{digitZero:X4}-siffror) måste avvisas — \\d hade släppt igenom den.");
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

    // ---------------------------------------------------------------
    // TryFromWrittenForm — the Art. 17 identifier normaliser (#842 CTO ruling 2026-07-14)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("5592804784", "5592804784")]     // the stored form, unchanged
    [InlineData("559280-4784", "5592804784")]    // canonical hyphenated org.nr form
    [InlineData("900101-1234", "9001011234")]    // canonical hyphenated personnummer form
    [InlineData("199001011234", "9001011234")]   // 12-digit century form → century stripped
    [InlineData("200010100000", "0010100000")]   // 20-century form
    [InlineData("19900101-1234", "9001011234")]  // century + hyphen, both undone
    [InlineData("  5592804784  ", "5592804784")] // surrounding whitespace trimmed
    public void TryFromWrittenForm_ForWrittenOrgNrForms_NormalisesToStoredForm(
        string written, string expected)
    {
        // Round 5's arm compared the WRITTEN form against the STORED form and silently matched
        // nothing ("556012-5790" never equals "5560125790"). The normaliser undoes presentation
        // — hyphen, century prefix, whitespace — and everything still funnels through Create, so
        // the format stays single-sourced.
        var result = OrganizationNumber.TryFromWrittenForm(written);

        result.ShouldNotBeNull();
        result.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)] // nothing
    [InlineData("")] // blank
    [InlineData("Magnus Fagerberg")] // a name — falls back to free-text matching
    [InlineData("magnus@skill.se")] // an email
    [InlineData("0730429030x")] // ten digits + junk
    [InlineData("559280478")] // 9 digits
    [InlineData("55928047841")] // 11 digits (not a 12-digit century form)
    [InlineData("189001011234")] // 12 digits but 18xx — not an accepted century
    [InlineData("5592-804784")] // hyphen NOT before the last four digits
    [InlineData("900101+1234")] // the 100+ '+' separator — deliberately unhandled
    public void TryFromWrittenForm_ForNonOrgNrShapes_ReturnsNull(string? written)
    {
        // Null means "not an org.nr identifier": the erasure falls back to the free-text
        // channels and the operator sees the honest zero on the dry run — never a guess.
        OrganizationNumber.TryFromWrittenForm(written).ShouldBeNull();
    }

    [Fact]
    public void TryFromWrittenForm_ForPhoneNumber_ReturnsTenDigitValue_AndThatIsDocumented()
    {
        // A Swedish mobile number IS ten digits ("0730429030"), so it normalises to a "valid"
        // org.nr shape. That is accepted, deliberately: the org.nr channels then run an EXACT
        // match against columns that hold only validated org.nr, where a phone number matches
        // nothing — an over-inclusive detector feeding an exact matcher is safe in precisely
        // the direction that matters. This test pins the behaviour so nobody "fixes" the
        // detector with a checksum and silently drops real org.nr forms Luhn would reject.
        var result = OrganizationNumber.TryFromWrittenForm("0730429030");

        result.ShouldNotBeNull();
        result.Value.ShouldBe("0730429030");
    }
}
