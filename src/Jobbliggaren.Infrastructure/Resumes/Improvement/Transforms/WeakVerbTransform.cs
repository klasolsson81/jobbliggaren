using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// A2/C3 weak→strong verb (Fas 4 STEG 10, F4-10): for each DESCRIPTION bullet that OPENS with a
/// weak verb from the F4-7 verb mapping, proposes its curated <c>SuggestedStrong</c>.
/// KnowledgeBank provenance — the <c>After</c> is the verbatim KB value, never synthesised.
/// The scored bullets are <see cref="ReviewText.DescriptionLines"/> — the SAME unit the F4-9 A2
/// review rule reads (#487 review side, #534 improve side) — so the header/period/organisation
/// lines are excluded. Reading the whole RawText block matched the job TITLE and so the rewrite
/// never fired in production (#534). Weak verbs are multi-word phrases that don't lexemise
/// cleanly, so the match is a word-boundary prefix (parity the A2 rule); each bullet is first
/// tokenised via the language-aware NLP tier (sv/en both wired), so the analysis dispatches
/// correctly.
/// </summary>
internal sealed class WeakVerbTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.WeakVerbUpgrade;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var index = 0;
        foreach (var experience in context.Content.Experience)
        {
            // #534: score the DESCRIPTION bullets (the same unit the F4-9 A2 review rule reads via
            // ReviewText.DescriptionLines, #487) — NOT the whole RawText block. On a real parsed CV
            // RawText opens with the title/organisation header, so reading the block matched the job
            // TITLE and the rewrite never fired in production. DescriptionLines already excludes the
            // header, the period line, and the organisation-on-its-own line, and trims each line.
            foreach (var bullet in ReviewText.DescriptionLines(experience))
            {
                // Language-aware NLP-tier tokenisation (dispatches sv/en; never throws for either).
                if (context.Analyzer.ToLexemes(bullet, context.Language).Count == 0)
                {
                    continue;
                }

                // #494: only propose a literal rewrite for a DROP-IN-SAFE pair (same valency/rection,
                // e.g. "var ansvarig för" → "ansvarade för"). A non-drop-in weak opener (a double
                // finite verb, or a role-overreach ADR 0071 forbids inventing) is still FLAGGED by the
                // F4-9 A2 review verdict, but the improve engine proposes NO rewrite for it (no synthesis).
                var match = context.Verbs.WeakVerbs
                    .FirstOrDefault(w => w.DropInSafe && ReviewText.StartsWithWord(bullet, w.Weak));
                if (match is null)
                {
                    continue;
                }

                // DescriptionLines already trims each line, so the opener is the verbatim first
                // Weak.Length chars — StartsWithWord matched exactly that word-bounded prefix, so this
                // is the cited Before span (equals the evidence Quote, satisfying the FromKnowledgeBank
                // guard). `index` runs across ALL bullets of ALL experiences so every targetId is unique.
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
}
