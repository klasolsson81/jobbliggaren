using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #478 Low — RenderProfile input must fail loud. <c>Enum.TryParse</c> accepted numeric strings
/// ("2", and even "0"/"1" that map to defined members), silently coercing a bad profile into an
/// undefined/wrong enum that yields an empty review/render. <see cref="RenderProfileNames.IsValidName"/>
/// accepts ONLY the exact member names, case-sensitive — the single SPOT the four query validators share.
///
/// Beyond the numeric surface, <c>Enum.TryParse</c> silently accepts three further shapes the
/// ordinal-exact helper must reject: (1) leading/trailing whitespace that <c>Enum.TryParse</c>
/// TRIMS before matching (" Ats" -> Ats), (2) comma flag-lists it OR-combines even though
/// <see cref="RenderProfile"/> is not <c>[Flags]</c> ("Ats,Visual" -> Ats|Visual == Visual), and
/// (3) signed / whitespace-wrapped numerics ("+1", " 2 "). All are locked below.
/// </summary>
public class RenderProfileNamesTests
{
    [Theory]
    [InlineData("Ats")]
    [InlineData("Visual")]
    public void IsValidName_ShouldAcceptExactMemberNames(string value) =>
        RenderProfileNames.IsValidName(value).ShouldBeTrue();

    [Theory]
    [InlineData("0")]       // numeric that maps to Ats — pre-fix Enum.TryParse accepted it
    [InlineData("1")]       // numeric that maps to Visual
    [InlineData("2")]       // numeric with no defined member — pre-fix accepted it, yielding an empty result
    [InlineData("-1")]
    [InlineData("ats")]     // case variant
    [InlineData("VISUAL")]
    [InlineData("Both")]    // a RubricProfile member, not a RenderProfile
    [InlineData("Pdf")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidName_ShouldRejectNumericCaseVariantAndUnknown(string value) =>
        RenderProfileNames.IsValidName(value).ShouldBeFalse();

    [Theory]
    [InlineData(" Ats")]    // leading space — Enum.TryParse TRIMS then accepts; ordinal-exact must not
    [InlineData("Ats ")]    // trailing space
    [InlineData(" Ats ")]   // both
    [InlineData(" Visual")]
    [InlineData("Visual ")]
    [InlineData("\tAts")]   // tab
    [InlineData("Ats\n")]   // newline
    [InlineData(" 2 ")]     // whitespace-wrapped numeric — Enum.TryParse trims to "2" then accepts
    public void IsValidName_ShouldRejectWhitespacePaddedInput(string value) =>
        RenderProfileNames.IsValidName(value).ShouldBeFalse();

    [Theory]
    [InlineData("Ats,Visual")]   // Enum.TryParse OR-combines list members (0|1 == 1 == Visual) despite no [Flags]
    [InlineData("Ats, Visual")]  // element whitespace is trimmed per member
    [InlineData("Visual,Ats")]
    [InlineData("Ats,Ats")]
    [InlineData("0,1")]          // numeric flag-list
    [InlineData("Ats,Both")]
    public void IsValidName_ShouldRejectCommaSeparatedList(string value) =>
        RenderProfileNames.IsValidName(value).ShouldBeFalse();

    [Theory]
    [InlineData("+1")]                      // signed numeric — Enum.TryParse accepts leading sign
    [InlineData("+0")]
    [InlineData("0x1")]                     // hex is NOT accepted by Enum.TryParse (rejected both sides)
    [InlineData("1.0")]                     // not an integer
    [InlineData("99999999999999999999")]    // overflows Int32 — rejected both sides, locked for robustness
    [InlineData("Ats;Visual")]              // semicolon is not the list separator
    [InlineData("Visual\0")]                // embedded NUL
    public void IsValidName_ShouldRejectSignedHexOverflowAndMalformedNumerics(string value) =>
        RenderProfileNames.IsValidName(value).ShouldBeFalse();

    [Fact]
    public void IsValidName_ShouldRejectNull() =>
        RenderProfileNames.IsValidName(null).ShouldBeFalse();

    // --- Positive / drift guards: the ONLY valid inputs are exactly the enum member names. ---

    [Fact]
    public void IsValidName_ShouldAcceptEveryDefinedEnumMemberName()
    {
        foreach (var name in Enum.GetNames<RenderProfile>())
            RenderProfileNames.IsValidName(name).ShouldBeTrue($"'{name}' is a defined RenderProfile member");
    }

    [Fact]
    public void ValidNames_ShouldBeExactlyAtsAndVisual()
    {
        // Drift guard: the helper's contract is "exactly the RenderProfile member names", and the
        // four validators surface a hardcoded "'Ats' eller 'Visual'" message. Adding or renaming a
        // member must fail this test so both the accept-set AND that Swedish message are re-examined
        // deliberately, never silently widened.
        Enum.GetNames<RenderProfile>().ShouldBe(["Ats", "Visual"]);
    }
}
