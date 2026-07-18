using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b #891 (ADR 0108) — <see cref="FontNameNormalizer"/>, the parser-FORM reduction of a raw
/// PdfPig font name to a comparable family token for the D3 allowlist match. It strips a leading
/// 6-uppercase subset tag, drops the closed style/weight/tech token set (split on -/,/space and
/// camelCase), concatenates the remaining family tokens and lowercases (InvariantCulture). The
/// D3 rule compares the result to each normalised allowlist entry for EXACT equality, so the two
/// load-bearing properties are: an unresolvable name yields "" (matches nothing → Warn, never a
/// fabricated Pass), and a width variant ("Arial Narrow") stays distinct from its base family.
/// </summary>
public class FontNameNormalizerTests
{
    // ===============================================================
    // Resolvable names → their comparable family token
    // ===============================================================

    [Theory]
    [InlineData("ABCDEF+Arial-BoldMT", "arial")]      // subset tag + style + tech suffix all stripped
    [InlineData("BCDEEE+Calibri-Bold", "calibri")]    // subset tag + style
    [InlineData("TimesNewRomanPSMT", "timesnewroman")] // camelCase split + PSMT tech token dropped
    [InlineData("Helvetica-Oblique", "helvetica")]    // style suffix dropped
    [InlineData("Arial-SemiBold", "arial")]           // Semi + Bold both dropped (camelCase split)
    [InlineData("Calibri", "calibri")]
    [InlineData("Arial", "arial")]
    [InlineData("Verdana", "verdana")]
    [InlineData("ArialNarrow", "arialnarrow")]        // Narrow is a WIDTH, kept → does NOT match "arial"
    public void Normalize_ShouldReduceToTheComparableFamilyToken(string raw, string expected)
    {
        FontNameNormalizer.Normalize(raw).ShouldBe(expected);
    }

    // ===============================================================
    // Unresolvable names → "" (matches nothing → the §5-honest non-match)
    // ===============================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bold")]   // a lone style token reduces to nothing
    public void Normalize_ShouldReturnEmpty_WhenTheNameIsNullBlankOrFullyStripped(string? raw)
    {
        FontNameNormalizer.Normalize(raw).ShouldBe(string.Empty);
    }

    // ===============================================================
    // The observed↔allowlist agreement the exact-match predicate relies on
    // ===============================================================

    [Fact]
    public void Normalize_ShouldMapEveryShippedAllowlistEntryToANonEmptyDistinctToken()
    {
        // The allowlist is DATA (cv-conventions) — read from the real asset, never a literal list.
        var allowlist = RealCvConventionsProvider().GetConventions().FontAllowlist;
        allowlist.ShouldNotBeEmpty();

        var tokens = allowlist.Select(FontNameNormalizer.Normalize).ToList();

        tokens.ShouldAllBe(token => token.Length > 0);
        tokens.ShouldBeUnique();
    }

    [Fact]
    public void Normalize_ShouldAgreeBetweenObservedAndAllowlistFormsOfTimesNewRoman()
    {
        // A subset-tagged PSMT-suffixed observed name and the clean allowlist entry must normalise
        // to the SAME token — otherwise the exact-equality D3 match could never fire for it.
        var observed = FontNameNormalizer.Normalize("TimesNewRomanPSMT");
        var allowlisted = FontNameNormalizer.Normalize("Times New Roman");

        observed.ShouldBe(allowlisted);
        observed.ShouldBe("timesnewroman");
    }
}
