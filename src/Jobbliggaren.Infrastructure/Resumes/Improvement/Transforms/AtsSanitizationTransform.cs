using System.Globalization;
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

        var firstOffending = -1;
        var count = 0;
        for (var i = 0; i < rawText.Length; i++)
        {
            if (!IsNonStandard(rawText[i]))
            {
                continue;
            }

            count++;
            if (firstOffending < 0)
            {
                firstOffending = i;
            }
        }

        if (count == 0)
        {
            yield break;
        }

        var quote = rawText.Substring(firstOffending, 1);
        var evidence = new TextSpanEvidence(
            new TextSpan(firstOffending, 1, quote),
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
    // letters/digits/punctuation are deliberately excluded.
    private static bool IsNonStandard(char c)
    {
        if (c <= 'ÿ')
        {
            return false;
        }

        return CharUnicodeInfo.GetUnicodeCategory(c) switch
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
