using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// D6 standard headings (Fas 4 STEG 10, F4-10): a raw-text line the parsing LEXICON recognises as a
/// section heading, but written in non-standard case (e.g. ALL-CAPS), is proposed normalised to
/// standard case. The proposed <c>After</c> is a PURE case transform of the user's own text (never a
/// synonym remap — that would be synthesis); the structural-transform guard re-runs the transform to
/// verify it.
///
/// <para><b>Recognition comes from the lexicon, and it used to come from the RENDERER (8b.4b fix).</b>
/// This transform derived its canonical set from <c>CvRenderStrings.SectionHeadings()</c> — the PDF
/// renderer's localised export labels — while the D6 RULE recognises headings through the lexicon.
/// One criterion, two tables, and they disagreed: <c>"ERFARENHET"</c> is a lexicon synonym of
/// <c>experience</c>, so the rule saw it and the transform did not, and its case was never
/// normalised. Every free section 8b.4a added (<c>projekt</c>, <c>legitimation</c>, <c>korkort</c>)
/// was likewise invisible here. A rendering table used as a recognition oracle is a
/// separation-of-concerns violation, not a §5 "list in C#" one — and it is fixed by reading the
/// owner of recognition (ADR 0107 §3), which is the lexicon.</para>
/// </summary>
internal sealed class HeadingNormalizationTransform : ICvTransform
{
    private readonly CvParsingLexiconData _lexicon;

    public HeadingNormalizationTransform(CvParsingLexiconData lexicon) =>
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));

    public ProposedChangeKind Kind => ProposedChangeKind.HeadingNormalization;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var rawText = context.RawText;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            yield break;
        }

        var index = 0;
        foreach (var line in rawText.Split('\n'))
        {
            var heading = line.Trim();

            // The lexicon's OWN normaliser (the one the segmenter runs), against the lexicon's OWN
            // vocabulary: typed headings and free-section headings alike. A caller-side lowercase
            // could drift from it — and did: it never stripped the trailing colon the lexicon strips.
            var lexicalKey = CvParsingLexiconLoader.NormalizeHeading(line);
            if (lexicalKey.Length == 0
                || (!_lexicon.HeadingMap.ContainsKey(lexicalKey)
                    && !_lexicon.FreeSectionIdByHeading.ContainsKey(lexicalKey)))
            {
                continue;
            }

            // A COMPOUND heading may lead with an acronym the user capitalised on purpose —
            // "IT-kompetenser" is a lexicon synonym of `skills`, and NormalizeCase ("first letter up,
            // the rest down") would propose "It-kompetenser". The engine would be DEGRADING a
            // correctly written heading. Before 8b.4b the render table did not hold the synonym, so
            // the line was simply invisible; widening recognition to the lexicon brought the case
            // transform along with it, over a vocabulary that no longer satisfies its assumption.
            //
            // The guard is on FORM, not on a name-list: a hyphen is what makes the canonical
            // capitalisation unknowable from the text alone. The general answer is a display form per
            // synonym in the lexicon — knowledge-bank data, a different change-reason, filed
            // separately. Until then the engine declines to guess, which is the honest failure mode:
            // a heading it cannot improve is a heading it leaves alone.
            if (heading.Contains('-', StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = NormalizeCase(heading);
            if (string.Equals(heading, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            var evidence = ReviewText.Span(rawText, heading, "rubrik i icke-standard versalisering");

            yield return ProposedChange.FromStructuralOp(
                targetId: $"heading:{index++}",
                kind: ProposedChangeKind.HeadingNormalization,
                category: RubricCategory.Structure,
                criterionId: context.CriterionIdFor("D6"),
                evidence: evidence,
                replacement: new ProposedReplacement(heading, normalized),
                operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, "rubrik"),
                rationale: "Standardisera rubrikens versalisering.",
                provenance: new StructuralTransformProvenance(StructuralTransformKind.NormalizeHeadingCase),
                pureTransform: NormalizeCase);
        }
    }

    // Pure, total case normalisation: capitalise the first letter, lowercase the rest. A pure
    // function of the input — introduces no new lexical content (no synthesis).
    private static string NormalizeCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
