using System.Text.RegularExpressions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// GPA strip (Fas 4 STEG 10, F4-10): a GPA/grade reference in an education entry's text is
/// proposed for removal (SE-market convention — a numeric GPA is uncommon and rarely helps a
/// Swedish CV). A PURE REMOVAL: it cites the offending span and the <c>RemoveGpa</c> operation,
/// carrying no rewritten text. Education with no GPA reference yields nothing.
/// </summary>
internal sealed partial class GpaStripTransform : ICvTransform
{
    [GeneratedRegex(
        @"\bGPA\b\s*:?\s*\d+(?:[.,]\d+)?(?:\s*/\s*\d+(?:[.,]\d+)?)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GpaRegex();

    public ProposedChangeKind Kind => ProposedChangeKind.GpaStrip;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var index = 0;
        var entryNumber = 0;
        foreach (var education in context.Content.Education)
        {
            entryNumber++;
            var text = education.RawText ?? string.Empty;
            var match = GpaRegex().Match(text);
            if (!match.Success)
            {
                continue;
            }

            var evidence = ReviewText.Span(text, match.Value, "GPA-referens (utbildning)");

            yield return ProposedChange.FromStructuralOp(
                targetId: $"gpa:{index++}",
                kind: ProposedChangeKind.GpaStrip,
                category: RubricCategory.Structure,
                criterionId: null,
                evidence: evidence,
                replacement: null,
                operation: new StructuralOperation(
                    StructuralTransformKind.RemoveGpa, $"utbildningspost {entryNumber}"),
                rationale: "Ta bort GPA/betygsreferensen (ovanligt och sällan till hjälp i ett svenskt CV).",
                provenance: new StructuralTransformProvenance(StructuralTransformKind.RemoveGpa),
                pureTransform: null);
        }
    }
}
