namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// Classifies whether a PdfPig font name denotes an ICON/DINGBAT typeface, for the D3 icon-body
/// Fail arm (#957, ADR 0108 amendment 2026-07-19). Like <see cref="FontNameNormalizer"/>'s
/// suffix set this is parser FORM in C#, NOT a §5 knowledge-bank list: "Wingdings is a dingbat
/// font" is intrinsic font-ecosystem mechanics — invariant across Swedish CV convention and ATS
/// policy — not a revisable domain recommendation (the font ALLOWLIST is the latter, which is
/// why THAT lives in cv-conventions). The set is CLOSED and deliberately conservative: only
/// unambiguous icon/dingbat brand tokens, matched by SUBSTRING over the normalised family token
/// (icon families ship many concatenated variants — "FontAwesome5Free-Solid",
/// "MaterialSymbolsOutlined" — so the allowlist's exact-equality predicate would under-detect).
/// Bare "symbol" is DELIBERATELY absent: it has a nonzero legit-text-font collision tail
/// ("Segoe UI Symbol"), and a miss here falls through to the rule's existing non-allowlisted
/// Warn — the safe under-claim (§5: a false Fail on a good CV is the over-claim sin; this set
/// trades recall for zero false positives).
/// </summary>
internal static class IconFontClassifier
{
    // Closed v1 token set — unambiguous icon/dingbat brands only (class doc covers the
    // deliberate "symbol" exclusion). Compared against FontNameNormalizer.Normalize output,
    // which is lowercase — entries stay lowercase.
    private static readonly string[] IconFontTokens =
    [
        "wingdings", "webdings", "dingbat", "fontawesome", "materialicons",
        "materialsymbol", "glyphicon", "icomoon", "entypo",
    ];

    /// <summary>
    /// True iff the raw font name normalises to a family token carrying an unambiguous
    /// icon-font brand token ("ABCDEF+Wingdings-Regular" → "wingdings" → true). A null/blank
    /// or unresolvable name is never an icon font — it falls to the rule's existing
    /// no-match Warn.
    /// </summary>
    internal static bool IsIconFont(string? raw)
    {
        var family = FontNameNormalizer.Normalize(raw);
        return family.Length > 0
            && IconFontTokens.Any(token => family.Contains(token, StringComparison.Ordinal));
    }
}
