using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// B4 personnummer (Fas 4 STEG 10, F4-10; ADR 0074 Invariant 1): when the parsed CV is flagged
/// as containing a personnummer/samordningsnummer, proposes its removal (GDPR — flag and ask to
/// remove, never to add). A PURE REMOVAL: no before/after replacement text, and the cited
/// evidence is the PII-safe count/structure ONLY — never the raw value or an offset (the
/// outcome carries neither by design).
/// </summary>
internal sealed class PersonnummerStripTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.PersonnummerStrip;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var outcome = context.Resume.Personnummer;
        if (!outcome.Found)
        {
            yield break;
        }

        yield return ProposedChange.FromStructuralOp(
            targetId: "personnummer:0",
            kind: ProposedChangeKind.PersonnummerStrip,
            category: RubricCategory.Structure,
            criterionId: context.CriterionIdFor("B4"),
            evidence: new StructuralEvidence($"Personnummer hittat ({outcome.Count} förekomst(er))."),
            replacement: null,
            operation: new StructuralOperation(
                StructuralTransformKind.RemovePersonnummer, $"{outcome.Count} personnummer-förekomst(er)"),
            rationale: "Ta bort personnummer ur CV:t (GDPR — Jobbliggaren ber dig aldrig lägga in det).",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.RemovePersonnummer),
            pureTransform: null);
    }
}
