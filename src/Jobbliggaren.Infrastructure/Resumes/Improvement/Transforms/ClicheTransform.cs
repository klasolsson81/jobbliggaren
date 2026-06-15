using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// A7 anti-cliché (Fas 4 STEG 10, F4-10): flags each cliché phrase from the F4-7 lexicon found
/// in the CV prose (profile + experience text) and proposes its curated
/// <c>BetterAlternative</c>. KnowledgeBank provenance — the <c>After</c> is the verbatim KB
/// value, never synthesised (CLAUDE.md §5).
/// </summary>
internal sealed class ClicheTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.ClicheReplacement;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var prose = Prose(context);
        if (prose.Length == 0)
        {
            yield break;
        }

        var lower = prose.ToLowerInvariant();
        var index = 0;
        foreach (var entry in context.Cliches.Entries)
        {
            if (!lower.Contains(entry.Phrase.ToLowerInvariant(), StringComparison.Ordinal))
            {
                continue;
            }

            var evidence = ReviewText.SpanCaseInsensitive(prose, entry.Phrase, $"klyscha: \"{entry.Phrase}\"");
            var before = evidence.Span.Quote;

            yield return ProposedChange.FromKnowledgeBank(
                targetId: $"cliche:{index++}",
                kind: ProposedChangeKind.ClicheReplacement,
                category: RubricCategory.Content,
                criterionId: context.CriterionIdFor("A7"),
                evidence: evidence,
                replacement: new ProposedReplacement(before, entry.BetterAlternative),
                rationale: entry.Why,
                provenance: new KnowledgeBankProvenance("cliche-list", context.Cliches.Version, entry.Phrase),
                resolvedKbValue: entry.BetterAlternative);
        }
    }

    private static string Prose(CvImprovementContext context)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.Content.Profile))
        {
            sb.AppendLine(context.Content.Profile);
        }

        foreach (var experience in context.Content.Experience)
        {
            if (!string.IsNullOrWhiteSpace(experience.RawText))
            {
                sb.AppendLine(experience.RawText);
            }
        }

        return sb.ToString();
    }
}
