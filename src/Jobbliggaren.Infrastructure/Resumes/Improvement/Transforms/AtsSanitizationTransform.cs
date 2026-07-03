using System.Globalization;
using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// ATS sanitization (Fas 4 STEG 10, F4-10): the ATS-plain rendering must drop non-standard
/// glyphs (decorative bullets, smart symbols, arrows, box-drawing, private-use) that an ATS
/// parser mangles. Proposes a single <c>StripNonStandardChars</c> structural removal citing the
/// first offending run. NEVER touches the user's words — only glyphs ABOVE Latin-1 in symbol /
/// control / private-use categories qualify, so Swedish letters (åäö, ÅÄÖ) and ordinary
/// punctuation (incl. the en/em dash) are preserved. ATS profile only (gated by the engine).
/// </summary>
internal sealed class AtsSanitizationTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.AtsSanitization;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var rawText = context.RawText;
        if (string.IsNullOrEmpty(rawText))
        {
            yield break;
        }

        // Iterate Unicode SCALAR VALUES (runes), not UTF-16 code units: the most ATS-hostile
        // glyphs — emoji — live in the astral planes and encode as surrogate PAIRS. A per-char
        // loop sees each half as an isolated Surrogate (never a symbol category) and would miss
        // every emoji; it would also Substring() a lone surrogate into an invalid quote (#478 Low).
        var firstOffending = -1;
        var firstOffendingLength = 0;
        var count = 0;
        var offset = 0;
        foreach (var rune in rawText.EnumerateRunes())
        {
            if (IsNonStandard(rune))
            {
                count++;
                if (firstOffending < 0)
                {
                    firstOffending = offset;
                    firstOffendingLength = rune.Utf16SequenceLength;
                }
            }

            offset += rune.Utf16SequenceLength;
        }

        if (count == 0)
        {
            yield break;
        }

        // The cited span quotes the WHOLE offending rune (an astral glyph is 2 UTF-16 units), so
        // the quote is always a valid string and the offset/length line up with RawText's char indices.
        var quote = rawText.Substring(firstOffending, firstOffendingLength);
        var evidence = new TextSpanEvidence(
            new TextSpan(firstOffending, firstOffendingLength, quote),
            $"{count} icke-standardtecken som en ATS-parser kan misstolka");

        yield return ProposedChange.FromStructuralOp(
            targetId: "ats:0",
            kind: ProposedChangeKind.AtsSanitization,
            category: RubricCategory.AtsParsability,
            criterionId: null,
            evidence: evidence,
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.StripNonStandardChars, "rå CV-text"),
            rationale: "Ta bort icke-standardtecken så att ATS-system kan tolka texten korrekt.",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.StripNonStandardChars),
            pureTransform: null);
    }

    // Non-standard for an ATS parser = a glyph ABOVE Latin-1 (so åäö and ASCII are always safe)
    // in a symbol / control / format / private-use category. The en/em dash (Pd) and ordinary
    // letters/digits/punctuation are deliberately excluded. Astral-plane emoji classify here as
    // OtherSymbol once seen as a whole rune; a Rune can never BE a lone surrogate — EnumerateRunes
    // folds any ill-formed UTF-16 fragment to U+FFFD (also OtherSymbol), so a broken glyph is
    // flagged too, which is correct (malformed text is ATS-hostile).
    private static bool IsNonStandard(Rune rune)
    {
        if (rune.Value <= 0xFF)
        {
            return false;
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.MathSymbol => true,
            UnicodeCategory.OtherSymbol => true,
            UnicodeCategory.ModifierSymbol => true,
            UnicodeCategory.CurrencySymbol => true,
            UnicodeCategory.PrivateUse => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Control => true,
            _ => false,
        };
    }
}
