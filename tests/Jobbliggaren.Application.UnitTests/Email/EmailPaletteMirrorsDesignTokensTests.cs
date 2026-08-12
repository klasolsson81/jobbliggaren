using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// Pins the email palette against <c>globals.css</c> (#183, 2026-08-12 — design-reviewer Minor 1).
///
/// <para>
/// An email cannot read <c>--jp-*</c> custom properties, so <see cref="EmailHtml"/> holds hex
/// literals. That is ratified in DESIGN.md § E-post. What was NOT handled is drift: the seven
/// literals were verified correct by hand on 2026-08-12, and nothing stopped them rotting the moment
/// someone edited a token. The web CSS guard cannot reach C#, so a hand check was the only control,
/// and a hand check is a measurement with an expiry date.
/// </para>
///
/// <para>
/// The precedent is <c>CvPalette</c>/<c>CvPaletteTests</c>: the same kind of copied design values in
/// Infrastructure, made CHECKABLE rather than merely declared. This is the same move, and it is the
/// PR's own thesis applied to itself — a ground a test can hold.
/// </para>
///
/// <para>
/// <b>When this fails, the email is wrong and the token is right.</b> The direction matters: a token
/// edit is a DESIGN.md decision, and this test's job is to make the email follow it rather than to
/// argue with it. Update the literal in <see cref="EmailHtml"/>, never the expectation here.
/// </para>
/// </summary>
public class EmailPaletteMirrorsDesignTokensTests
{
    /// <summary>
    /// Each email literal and the token it claims to mirror, exactly as <see cref="EmailHtml"/>
    /// names it in the doc comment above each constant.
    /// </summary>
    public static TheoryData<string, string> Mirror() => new()
    {
        { "--jp-canvas", EmailHtml.Canvas },
        { "--jp-surface", EmailHtml.Surface },
        { "--jp-border", EmailHtml.Border },
        { "--jp-border-soft", EmailHtml.BorderSoft },
        { "--jp-navy-800", EmailHtml.Heading },
        { "--jp-ink-1", EmailHtml.Ink },
        { "--jp-accent-800", EmailHtml.Accent },
    };

    [Theory]
    [MemberData(nameof(Mirror))]
    public void EmailPalette_ForEveryLiteral_MatchesTheTokenItNames(string token, string literal)
    {
        var css = LightRootBlock();

        // `--jp-border` must not match `--jp-border-soft`, so the name is anchored on the colon.
        var match = Regex.Match(
            css,
            $@"{Regex.Escape(token)}\s*:\s*(#[0-9A-Fa-f]{{6}})\s*;",
            RegexOptions.CultureInvariant);

        match.Success.ShouldBeTrue(
            $"{token} was not found in the light :root block of globals.css — if the token was "
            + "renamed, EmailHtml's doc comment names a token that no longer exists");

        // Case-insensitive: CSS hex is case-free, so `#f4f6fa` and `#F4F6FA` are the same colour and a
        // case difference must not read as drift.
        literal.ShouldBe(
            match.Groups[1].Value,
            StringCompareShould.IgnoreCase);
    }

    [Fact]
    public void EmailPalette_EveryColourConstant_IsCoveredByTheMirror()
    {
        // Mirror() is a hand-written list, which closes drift in the values and NOT in the set: add an
        // eighth colour constant to EmailHtml and every case above still passes while the new literal
        // is unpinned. That is the same growth-blindness the template guard had (code-reviewer, and
        // the precedent this file cites — CvPaletteTests iterates CvPalette.Pairs precisely so a new
        // pair cannot be forgotten).
        var hex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        var colourConstants = typeof(EmailHtml)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string?)f.GetRawConstantValue())
            .Where(v => v is not null && hex.IsMatch(v))
            .ToList();

        colourConstants.ShouldNotBeEmpty("reflection found no colour constants, so this fact is vacuous");

        colourConstants.Count.ShouldBe(
            Mirror().Count,
            "EmailHtml has a colour constant that Mirror() does not pin. Add it there with the token "
            + "it mirrors, or the literal drifts from globals.css unnoticed.");
    }

    [Fact]
    public void EmailPalette_TheOracle_ActuallyReadsGlobalsCss()
    {
        // Without this the suite above is fail-open in the ordinary way: if the file could not be
        // found and the helper returned empty, every Theory case would fail on `match.Success` with a
        // message about a renamed token, which reads as a real finding and is not one. This states the
        // difference plainly.
        var css = LightRootBlock();

        css.ShouldNotBeNullOrWhiteSpace();
        css.ShouldContain("--jp-accent-800");

        // The dark block must be OUTSIDE the extracted region, or a dark override could satisfy a
        // light expectation. Asserted on the selector's real form (with its brace), not on the bare
        // string: the bare string appears in `@custom-variant` and in comments long before the block,
        // and taking it as the boundary is precisely the bug this fact caught.
        css.ShouldNotContain("[data-theme=\"dark\"] {");

        // And the region really is bounded rather than the whole file: --jp-ink-1 is defined once in
        // light and again in dark, so exactly one definition may survive the extraction.
        Regex.Count(css, @"--jp-ink-1\s*:").ShouldBe(1);
    }

    /// <summary>
    /// The light palette only: the first <c>:root { … }</c> block, delimited by brace matching.
    /// <para>
    /// <b>The obvious implementation is wrong, and the oracle fact below is what caught it.</b>
    /// "Everything before the first <c>[data-theme="dark"]</c>" looks equivalent and is not: the
    /// string appears on line 11 inside <c>@custom-variant dark (…)</c> and again inside prose
    /// comments, hundreds of lines before the dark block itself, so that cut discarded the entire
    /// light palette and every token then read as "renamed". Comments are stripped before matching so
    /// a brace inside prose cannot end the block early.
    /// </para>
    /// </summary>
    private static string LightRootBlock()
    {
        var css = Regex.Replace(
            File.ReadAllText(GlobalsCssPath()), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        var selectorAt = css.IndexOf(":root", StringComparison.Ordinal);
        if (selectorAt < 0) return string.Empty;

        var openAt = css.IndexOf('{', selectorAt);
        if (openAt < 0) return string.Empty;

        var depth = 0;
        for (var i = openAt; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[openAt..i];
        }

        return string.Empty;
    }

    /// <summary>
    /// Walks up from the test binary until the repo root is found. The test project's own directory
    /// depth is not hardcoded, so moving the project does not silently break the lookup into a
    /// "renamed token" failure.
    /// </summary>
    private static string GlobalsCssPath()
    {
        const string Relative = "web/jobbliggaren-web/src/app/globals.css";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, Relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {Relative} by walking up from {AppContext.BaseDirectory}. "
            + "The email palette pin needs the repo checkout to be present.");
    }
}
