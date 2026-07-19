using System.Globalization;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// D6 standard headings (Fas 4 STEG 10, F4-10): a raw-text line the parsing LEXICON recognises as a
/// section heading, but written in non-standard case (e.g. ALL-CAPS), is proposed normalised to
/// standard case. TWO arms, both no-synthesis (CLAUDE.md §5):
/// <list type="bullet">
///   <item><b>Display form (#893, lexicon v6).</b> A synonym the lexicon carries a canonical
///   <c>displayForm</c> for is proposed AS that form via <see cref="ProposedChange.FromKnowledgeBank"/>,
///   because an acronym's casing (<c>"it-kompetenser"</c> → <c>"IT-kompetenser"</c>) is not recoverable
///   by a case rule. The loader pins the form to differ from the synonym ONLY by letter case, so it is a
///   pure RE-CASING of the user's own word, never a synonym remap.</item>
///   <item><b>Case transform (fallback).</b> Otherwise the <c>After</c> is a PURE case transform of the
///   user's own text via <see cref="ProposedChange.FromStructuralOp"/> (re-verified by re-running the
///   transform). A COMPOUND heading with no display form is DECLINED — its canonical capitalisation is
///   unknowable from the text alone — the honest failure mode, never a guess.</item>
/// </list>
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

            // ARM 1 — display form (#893, lexicon v6). A synonym the lexicon carries a canonical
            // display form for is proposed AS that form (knowledge-bank-sourced, not a pure transform).
            // "it-kompetenser" is a lexicon synonym of `skills` whose casing ("IT-kompetenser")
            // NormalizeCase cannot recover ("first letter up, the rest down" → "It-kompetenser",
            // DEGRADING a correctly-written heading). The loader pins the form to differ from lexicalKey
            // ONLY by letter case, so this RE-CASES the user's own word — it never remaps it to a
            // different synonym (that would be synthesis, CLAUDE.md §5). This is why the After is KB-sourced:
            // FromStructuralOp's After==pureTransform(Before) guard cannot express a curated casing.
            if (_lexicon.DisplayFormByHeading.TryGetValue(lexicalKey, out var displayForm))
            {
                // Fire only on a GENUINE casing difference — symmetric with ARM 2's idempotence guard
                // (heading == NormalizeCase(heading)). D6's criterion is versalisering, so strip the
                // trailing separators NormalizeHeading ignores before comparing: a heading already in
                // canonical case ("IT-kompetenser" OR "IT-kompetenser:") yields no proposal, because
                // "standardise the casing" would be a false rationale when only a colon differs.
                if (string.Equals(heading.TrimEnd(':', '.', ' ', '\t'), displayForm, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return ProposedChange.FromKnowledgeBank(
                    targetId: $"heading:{index++}",
                    kind: ProposedChangeKind.HeadingNormalization,
                    category: RubricCategory.Structure,
                    criterionId: context.CriterionIdFor("D6"),
                    evidence: ReviewText.Span(rawText, heading, "rubrik i icke-standard versalisering"),
                    replacement: new ProposedReplacement(heading, displayForm),
                    rationale: "Standardisera rubrikens versalisering till dess kanoniska form.",
                    provenance: new KnowledgeBankProvenance(
                        "cv-parsing-lexicon",
                        _lexicon.Version.ToString(CultureInfo.InvariantCulture),
                        lexicalKey),
                    resolvedKbValue: displayForm);
                continue;
            }

            // ARM 2 — case transform, with the compound-heading fallback guard. No display form: a
            // COMPOUND heading's canonical capitalisation is unknowable from the text alone ("PL-SQL"
            // could be "PL-SQL", "Pl-SQL", …), so NormalizeCase would DEGRADE it. The guard is on FORM,
            // not a name-list — a hyphen is the signal — and the engine declines rather than guesses.
            // ARM 1 is how a specific hyphenated synonym is LIFTED out of this decline; until a display
            // form is authored, a heading it cannot improve is a heading it leaves alone.
            if (heading.Contains('-', StringComparison.Ordinal))
            {
                continue;
            }

            var normalized = NormalizeCase(heading);
            if (string.Equals(heading, normalized, StringComparison.Ordinal))
            {
                continue;
            }

            yield return ProposedChange.FromStructuralOp(
                targetId: $"heading:{index++}",
                kind: ProposedChangeKind.HeadingNormalization,
                category: RubricCategory.Structure,
                criterionId: context.CriterionIdFor("D6"),
                evidence: ReviewText.Span(rawText, heading, "rubrik i icke-standard versalisering"),
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
