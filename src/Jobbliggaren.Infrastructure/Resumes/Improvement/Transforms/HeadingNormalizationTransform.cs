using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// D6 standard headings (Fas 4 STEG 10, F4-10): a raw-text line that matches a canonical section
/// heading (<see cref="CvRenderStrings.SectionHeadings"/>, the single localised source) but is
/// in non-standard case (e.g. ALL-CAPS) is proposed normalised to standard case. The proposed
/// <c>After</c> is a PURE case transform of the user's own text (never a synonym remap — that
/// would be synthesis); the structural-transform guard re-runs the transform to verify it.
/// </summary>
internal sealed class HeadingNormalizationTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.HeadingNormalization;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var rawText = context.RawText;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            yield break;
        }

        var canonical = CvRenderStrings.SectionHeadings(context.Resume.DetectedLanguage)
            .Select(h => h.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var index = 0;
        foreach (var line in rawText.Split('\n'))
        {
            var heading = line.Trim();
            if (heading.Length == 0 || !canonical.Contains(heading.ToLowerInvariant()))
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
