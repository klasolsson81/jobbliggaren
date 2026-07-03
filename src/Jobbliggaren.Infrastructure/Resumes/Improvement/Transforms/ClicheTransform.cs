using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// A7 anti-cliché (Fas 4 STEG 10, F4-10): for each cliché phrase from the F4-7 lexicon found on a
/// WORD BOUNDARY in the CV prose (profile + experience text), proposes its curated
/// <c>DropInReplacement</c> — but ONLY when the entry carries a genuine same-meaning drop-in
/// (#495). KnowledgeBank provenance — the <c>After</c> is that verbatim drop-in, never the
/// advisory <c>Guidance</c> (which may carry illustrative numbers / a meta-instruction) and never
/// synthesised (CLAUDE.md §5). A cliché with no drop-in is still FLAGGED by the F4-9 A7 review;
/// the improve engine simply proposes no rewrite for it.
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

        var index = 0;
        foreach (var entry in context.Cliches.Entries)
        {
            // Propose a literal rewrite ONLY when the entry carries a genuine same-meaning
            // drop-in (#495). The advisory `Guidance` may embed illustrative numbers or a
            // meta-instruction ("Skriv siffrorna direkt") and must NEVER be applied verbatim —
            // that would inject qualifications the user never wrote (ADR 0071/0074, no synthesis).
            // With no drop-in, the F4-9 A7 review still FLAGS the cliché; the improve engine
            // proposes nothing. Today's asset carries no drop-in, so A7 carries the whole signal.
            if (entry.DropInReplacement is null)
            {
                continue;
            }

            // Word-bounded so "Social" never splices mid-word inside "sociala" (#496); EVERY
            // occurrence is proposed deterministically, left to right.
            foreach (var span in ReviewText.WordSpans(prose, entry.Phrase))
            {
                var evidence = new TextSpanEvidence(span, $"klyscha: \"{entry.Phrase}\"");

                yield return ProposedChange.FromKnowledgeBank(
                    targetId: $"cliche:{index++}",
                    kind: ProposedChangeKind.ClicheReplacement,
                    category: RubricCategory.Content,
                    criterionId: context.CriterionIdFor("A7"),
                    evidence: evidence,
                    replacement: new ProposedReplacement(span.Quote, entry.DropInReplacement),
                    rationale: entry.Why,
                    provenance: new KnowledgeBankProvenance("cliche-list", context.Cliches.Version, entry.Phrase),
                    resolvedKbValue: entry.DropInReplacement);
            }
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
