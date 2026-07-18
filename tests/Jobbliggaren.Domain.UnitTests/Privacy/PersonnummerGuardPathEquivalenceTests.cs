using System.Globalization;
using System.Text;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

/// <summary>
/// #650 (cv-v2 PR-1, Step 0 of epic #649) — combinatorial path-equivalence guard between the
/// FLAG path (the <c>Normalize → Scan</c> chain the import/write guard runs) and the REDACTION
/// path (<c>PersonnummerRedactor.Redact</c> via <c>PersonnummerScanner.ScanWithGaps</c>).
///
/// <para>Pins ONE direction only — the #465 superset invariant already pinned point-wise in
/// <c>PersonnummerRedactorTests</c>: every form the flag path detects, the redactor must also
/// mask (<c>Scan(Normalize(x)).Count &gt; 0</c> implies <c>Redact(x) != x</c> AND no significant
/// digit run survives in the output). The REVERSE implication is deliberately NOT asserted —
/// the redactor's legitimately wider coverage (e.g. an OCR gap the flag path misses) is
/// desirable, not a defect.</para>
///
/// <para>Instead of hand-picked vectors, this file deterministically generates the FULL
/// cartesian product bases x separators x gaps x zero-width noise x contexts (no random, no
/// time dependence) so a future divergence between the two paths on ANY combination of the
/// known personnummer obfuscation dimensions fails loudly here first. All base vectors are
/// SYNTHETIC Luhn-valid test numbers (parity <c>PersonnummerScannerTests</c> /
/// <c>PersonnummerRedactorTests</c>). Project rule: non-ASCII separators/spaces are written as
/// <c>\uXXXX</c> escapes — never literal Unicode space/dash characters in source.</para>
/// </summary>
public class PersonnummerGuardPathEquivalenceTests
{
    // Established synthetic Luhn-valid bases (Lead = digits before the separator position,
    // Tail = the final four digits): the 10-digit personnummer and samordningsnummer vectors
    // shared with PersonnummerScannerTests/PersonnummerRedactorTests, plus the full-century
    // 12-digit form. The samordningsnummer encodes day+60 (18+60=78).
    private static readonly (string Lead, string Tail, string Label)[] Bases =
    [
        ("811218", "9876", "10-digit personnummer"),
        ("19811218", "9876", "12-digit personnummer"),
        ("811278", "9873", "10-digit samordningsnummer (day+60)"),
    ];

    // Separator repertoire (#497): ASCII '-'/'+', plus the Unicode dashes Word/PDF emit.
    // Non-ASCII code points as \uXXXX escapes (project rule).
    private static readonly (string Sep, string Label)[] Separators =
    [
        ("", "none"),
        ("-", "U+002D HYPHEN-MINUS"),
        ("+", "U+002B PLUS (century separator)"),
        ("\u2010", "U+2010 HYPHEN"),
        ("\u2011", "U+2011 NON-BREAKING HYPHEN"),
        ("\u2012", "U+2012 FIGURE DASH"),
        ("\u2013", "U+2013 EN DASH"),
        ("\u2014", "U+2014 EM DASH"),
        ("\u2212", "U+2212 MINUS SIGN"),
    ];

    // Visible gap repertoire (#268 C1/#427): the Unicode space separators PDF/DOCX extraction
    // and this product's own digit-group formatting emit, bounded at two visible columns
    // (the bridged window; a 3+ column gap is the accepted #427 V3 residual, not generated).
    private static readonly (string Gap, string Label)[] Gaps =
    [
        ("", "none"),
        (" ", "U+0020 SPACE"),
        ("\u00A0", "U+00A0 NO-BREAK SPACE"),
        ("\u202F", "U+202F NARROW NO-BREAK SPACE"),
        ("\u2009", "U+2009 THIN SPACE"),
        ("\t", "U+0009 TAB"),
        ("  ", "double U+0020 SPACE"),
    ];

    // Zero-width format-char noise (#427 V2/#498a): \p{Cf} characters PDF/DOCX extraction
    // interleaves — in the gap region or INSIDE a digit group.
    private enum Noise
    {
        None,
        ZeroWidthSpaceInGap, // U+200B placed at the gap position
        ZeroWidthSpaceInsideDigitGroup, // U+200B inside the leading digit group
        ZeroWidthNoBreakSpaceInGap, // U+FEFF placed at the gap position
        ZeroWidthNoBreakSpaceInsideDigitGroup, // U+FEFF inside the leading digit group
    }

    private static readonly Noise[] Noises =
    [
        Noise.None,
        Noise.ZeroWidthSpaceInGap,
        Noise.ZeroWidthSpaceInsideDigitGroup,
        Noise.ZeroWidthNoBreakSpaceInGap,
        Noise.ZeroWidthNoBreakSpaceInsideDigitGroup,
    ];

    // Context wrappers: bare token, filename-wrapped (the #465 motivating shape for the
    // unencrypted source_file_name column) and sentence-wrapped (free CV prose). The wrappers
    // are digit-free so every ASCII digit in the composed text belongs to the personnummer.
    private static readonly (Func<string, string> Wrap, string Label)[] Contexts =
    [
        (token => token, "bare"),
        (token => $"CV_{token}.pdf", "filename-wrapped"),
        (token => $"Kandidatens nummer {token} finns i dokumentet.", "sentence-wrapped"),
    ];

    [Fact]
    public void FlagPathDetection_ImpliesRedactionStripsTheDigits_AcrossTheFullProduct()
    {
        var failures = new List<string>();
        var total = 0;
        var flagged = 0;

        foreach (var (lead, tail, baseLabel) in Bases)
        {
            foreach (var (sep, sepLabel) in Separators)
            {
                foreach (var (gap, gapLabel) in Gaps)
                {
                    foreach (var noise in Noises)
                    {
                        foreach (var (wrap, contextLabel) in Contexts)
                        {
                            total++;
                            var text = wrap(Compose(lead, tail, sep, gap, noise));

                            // The pinned direction only: flag fired => redaction happened.
                            // An UNFLAGGED combination is skipped, never failed — asserting
                            // the reverse would forbid the redactor's wider coverage.
                            if (PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text)).Count == 0)
                                continue;

                            flagged++;
                            var redacted = PersonnummerRedactor.Redact(text);

                            // "No significant digit run survives": the contexts are digit-free,
                            // so EVERY remaining ASCII digit would be a leaked personnummer
                            // digit. The lead/tail contains-checks are the task-level significant
                            // runs; the any-digit check catches a partial masking in between.
                            if (redacted == text
                                || redacted.Contains(lead, StringComparison.Ordinal)
                                || redacted.Contains(tail, StringComparison.Ordinal)
                                || redacted.Any(char.IsAsciiDigit))
                            {
                                failures.Add(
                                    $"{baseLabel} | sep={sepLabel} | gap={gapLabel} | " +
                                    $"noise={noise} | context={contextLabel} | text=\"{Escape(text)}\"");
                            }
                        }
                    }
                }
            }
        }

        // Product-size pin: the generator must actually enumerate the full cartesian product —
        // a silently shrunken dimension would hollow the guard out. Pinned as a literal (not
        // derived from the arrays) so removing a corpus entry fails here explicitly:
        // 3 bases x 9 separators x 7 gaps x 5 noise kinds x 3 contexts = 2835.
        total.ShouldBe(2835);

        // Anti-vacuous: the implication must have real weight — if the flag path suddenly
        // detected almost nothing, every case would pass vacuously while the guard rotted.
        // Evidence-based floor: at authoring time (2026-07-05, post the U+2012/U+2014 separator
        // and U+FEFF-inside-digit-group corpus additions) the flag path detected ALL 2835/2835
        // combinations. The floor is pinned below that (~78%) to leave headroom for a
        // legitimate, deliberate scanner tightening that unflags a slice of exotic forms,
        // while still failing loudly on a collapse-to-a-handful regression.
        flagged.ShouldBeGreaterThanOrEqualTo(2200);

        failures.ShouldBeEmpty(
            $"every flag-path-detected form must also be redacted (flag superset invariant, " +
            $"#465/#650). {flagged}/{total} combinations were flagged; divergent combinations: " +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryBaseVector_PlainDashSeparatedBareForm_IsFlaggedByTheFlagPath()
    {
        // Anti-stale anchor for the corpus itself: each base must be Luhn/date-valid and
        // detectable in its plainest form, else the product above degenerates silently.
        foreach (var (lead, tail, label) in Bases)
        {
            var text = $"{lead}-{tail}";
            PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
                .ShouldNotBeEmpty($"base vector must be flaggable in its plain form: {label} ({text})");
        }
    }

    [Fact]
    public void SamordningsnummerBase_IsDetectedWithSamordningsnummerKind()
    {
        // Keeps the "day+60" base honest: it must be classified as a samordningsnummer,
        // not merely pass as an ordinary personnummer.
        var match = PersonnummerScanner.Scan("811278-9873").ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    [Fact]
    public void NewlyHardenedShapes_FlagPathDetection_ImpliesRedaction_NoDecimalDigitSurvives()
    {
        // STEG 1 pnr-scanner hardening (#665 two-separator zero-space, #667 fullwidth \p{Nd}): the
        // two shapes newly reachable on the FLAG path in this change must ALSO satisfy the pinned
        // redaction-superset-of-flag invariant - flag fires => Redact strips it AND no decimal
        // digit (ASCII OR fullwidth) survives. Kept a focused product (not folded into the 2835-cell
        // matrix above) because the double-separator and the fullwidth-digit rendering do not fit
        // that generator's single-separator / ASCII-base cells. Contexts are digit-free, so every
        // surviving \p{Nd} would be a leaked personnummer digit. Fullwidth vectors built at runtime
        // (source stays ASCII-only, project rule).
        var shapes = new (string Token, string Label)[]
        {
            ("811218--9876", "#665 double-separator no-space (personnummer)"),
            ("811278--9873", "#665 double-separator no-space (samordningsnummer)"),
            ("19811218--9876", "#665 double-separator no-space (12-digit)"),
            (ToFullwidthDigits("811218-9876"), "#667 fullwidth personnummer"),
            (ToFullwidthDigits("811278-9873"), "#667 fullwidth samordningsnummer"),
            (ToFullwidthDigits("8112189876"), "#667 fullwidth contiguous"),
            (ToFullwidthDigits("811218") + "--" + ToFullwidthDigits("9876"),
                "#665+#667 fullwidth double-separator"),
        };

        var failures = new List<string>();
        foreach (var (token, label) in shapes)
        {
            foreach (var (wrap, contextLabel) in Contexts)
            {
                var text = wrap(token);

                // These are the newly-closed forms: the flag path MUST detect each.
                if (PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text)).Count == 0)
                {
                    failures.Add($"NOT FLAGGED: {label} | context={contextLabel} | text=\"{Escape(text)}\"");
                    continue;
                }

                var redacted = PersonnummerRedactor.Redact(text);
                if (redacted == text || redacted.Any(c => CharUnicodeInfo.GetDecimalDigitValue(c) >= 0))
                {
                    failures.Add(
                        $"NOT REDACTED / digit survived: {label} | context={contextLabel} | " +
                        $"text=\"{Escape(text)}\" | redacted=\"{Escape(redacted)}\"");
                }
            }
        }

        failures.ShouldBeEmpty(
            "every newly hardened flag-path-detected shape must also be redacted with no decimal " +
            "digit surviving (flag superset invariant, #665/#667):" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    // Fullwidth (U+FF10-U+FF19) rendering of the ASCII digits in the input, built at runtime so
    // the source stays ASCII-only (project rule: no literal Unicode). Non-digits pass through.
    private static string ToFullwidthDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)(0xFF10 + (chars[i] - '0'));
        }

        return new string(chars);
    }

    // Composes lead + separator + gap + tail with the noise dimension applied: a zero-width
    // char either rides at the gap position (after the separator, before the visible gap) or
    // sits INSIDE the leading digit group (after its second digit). All zero-width points as
    // \uXXXX escapes.
    private static string Compose(string lead, string tail, string sep, string gap, Noise noise)
    {
        var leadWithNoise = noise switch
        {
            Noise.ZeroWidthSpaceInsideDigitGroup => lead[..2] + "\u200B" + lead[2..],
            Noise.ZeroWidthNoBreakSpaceInsideDigitGroup => lead[..2] + "\uFEFF" + lead[2..],
            _ => lead,
        };

        var gapWithNoise = noise switch
        {
            Noise.ZeroWidthSpaceInGap => "\u200B" + gap,
            Noise.ZeroWidthNoBreakSpaceInGap => "\uFEFF" + gap,
            _ => gap,
        };

        return leadWithNoise + sep + gapWithNoise + tail;
    }

    // Failure-message readability: tabs and non-ASCII code points (the whole point of the
    // product) are invisible or ambiguous in test output — print them as \uXXXX escapes.
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= ' ' and <= '~')
                sb.Append(c);
            else
                sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
        }

        return sb.ToString();
    }
}
