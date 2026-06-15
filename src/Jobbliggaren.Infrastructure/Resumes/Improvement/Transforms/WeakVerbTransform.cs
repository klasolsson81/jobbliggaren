using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// A2/C3 weak→strong verb (Fas 4 STEG 10, F4-10): for each experience bullet that OPENS with a
/// weak verb from the F4-7 verb mapping, proposes its curated <c>SuggestedStrong</c>.
/// KnowledgeBank provenance — the <c>After</c> is the verbatim KB value, never synthesised.
/// Weak verbs are multi-word phrases that don't lexemise cleanly, so the match is a
/// word-boundary prefix (parity the F4-9 A2 rule); the bullet is first tokenised via the
/// language-aware NLP tier (sv/en both wired), so the analysis dispatches correctly.
/// </summary>
internal sealed class WeakVerbTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.WeakVerbUpgrade;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var index = 0;
        foreach (var experience in context.Content.Experience)
        {
            var bullet = experience.RawText?.Trim() ?? string.Empty;
            if (bullet.Length == 0)
            {
                continue;
            }

            // Language-aware NLP-tier tokenisation (dispatches sv/en; never throws for either).
            if (context.Analyzer.ToLexemes(bullet, context.Language).Count == 0)
            {
                continue;
            }

            var match = context.Verbs.WeakVerbs.FirstOrDefault(w => ReviewText.StartsWithWord(bullet, w.Weak));
            if (match is null)
            {
                continue;
            }

            var before = bullet[..match.Weak.Length];
            var evidence = ReviewText.Span(bullet, before, "inleds med ett svagt verb (se verb-mappningen)");

            yield return ProposedChange.FromKnowledgeBank(
                targetId: $"weakverb:{index++}",
                kind: ProposedChangeKind.WeakVerbUpgrade,
                category: RubricCategory.Content,
                criterionId: context.CriterionIdFor("A2"),
                evidence: evidence,
                replacement: new ProposedReplacement(before, match.SuggestedStrong),
                rationale: "Byt det svaga inledande verbet mot ett starkt handlingsverb.",
                provenance: new KnowledgeBankProvenance("verb-mapping", context.Verbs.Version, match.Weak),
                resolvedKbValue: match.SuggestedStrong);
        }
    }
}
