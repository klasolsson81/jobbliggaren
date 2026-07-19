using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #957 (the D3 Fail arm) — <see cref="IconFontClassifier"/>, the parser-FORM predicate that
/// decides whether a raw PdfPig font name belongs to an ICON font. Like
/// <c>FontNameNormalizer</c>'s <c>StyleAndTechTokens</c>, this is intrinsic font-ecosystem
/// mechanics (Wingdings is a symbol font in every language and under every ATS policy), so it
/// lives in C#, NOT as knowledge-bank DATA (contrast the font allowlist itself, which IS data).
/// It runs the SAME normaliser over the raw name and tests whether the result CONTAINS (Ordinal)
/// any token of a CLOSED v1 set: { wingdings, webdings, dingbat, fontawesome, materialicons,
/// materialsymbol, glyphicon, icomoon, entypo }. Bare "symbol" is DELIBERATELY excluded — it has
/// a nonzero legit-font collision tail (SegoeUISymbol, the Symbol body font), so a Symbol-body CV
/// falls through to D3's existing non-allowlisted Warn: under-claim is the §5-safe arm, since a
/// false Fail on a good CV is the over-claim sin.
/// </summary>
public class IconFontClassifierTests
{
    // ===============================================================
    // Icon fonts → TRUE (legacy symbol fonts + modern icon webfonts,
    // seen through subset tags, style suffixes, camelCase and spacing)
    // ===============================================================

    [Theory]
    [InlineData("Wingdings")]
    [InlineData("Wingdings 2")]                 // numbered variant, still contains "wingdings"
    [InlineData("ABCDEF+Wingdings-Regular")]    // subset tag + style suffix reduced by the normaliser
    [InlineData("Webdings")]
    [InlineData("ZapfDingbats")]                // camelCase split → contains "dingbat"
    [InlineData("FontAwesome5Free-Solid")]
    [InlineData("Font Awesome 6 Free")]         // spaced form → contains "fontawesome"
    [InlineData("MaterialIcons-Regular")]
    [InlineData("MaterialSymbolsOutlined")]     // contains "materialsymbol"
    [InlineData("glyphicons-halflings")]        // Bootstrap Glyphicons → contains "glyphicon"
    [InlineData("IcoMoon-Free")]
    [InlineData("Entypo")]
    public void IsIconFont_ShouldReturnTrue_WhenTheNameBelongsToAnIconFont(string raw)
    {
        IconFontClassifier.IsIconFont(raw).ShouldBeTrue();
    }

    // ===============================================================
    // Standard body fonts (and the deliberately-excluded "symbol"
    // collision tail) → FALSE. Null/blank normalise to "" → no match.
    // ===============================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Arial")]
    [InlineData("ABCDEF+Arial-BoldMT")]    // subset tag + style stripped → "arial"
    [InlineData("Calibri")]
    [InlineData("Garamond")]
    [InlineData("Lato")]
    [InlineData("ArialNarrow")]            // a WIDTH variant, not an icon font
    [InlineData("Symbol")]                 // DELIBERATELY excluded (legit-font collision tail) → falls through to Warn
    [InlineData("SegoeUISymbol")]          // the "...Symbol" tail the exclusion protects; "symbol" is not a token
    [InlineData("Times New Roman")]
    public void IsIconFont_ShouldReturnFalse_WhenTheNameIsNotAnIconFont(string? raw)
    {
        IconFontClassifier.IsIconFont(raw).ShouldBeFalse();
    }
}
