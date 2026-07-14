using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Sections;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// B1 section order (Fas 4 STEG 10, F4-10; un-inerted in Fas 4b 8b.4b). The CV's OBSERVED section
/// order is compared against the recommended order in <c>cv-conventions.v1.json</c> (the rubric's B1
/// <c>atsPassSignal</c> chain, made machine-readable — ADR 0108); when they deviate, a reorder is
/// proposed. Until 8b.4b there was no such data source and hardcoding an order in C# is forbidden
/// (CLAUDE.md §5), so this transform honestly proposed NOTHING.
///
/// <para><b>The order itself is computed by <see cref="SectionOrderAnalyzer"/>, which the REVIEW
/// engine's B1 rule reads too.</b> One knowledge piece, one home: the criterion that JUDGES the
/// order and the transform that PROPOSES a new one verdict against the same computation, so the
/// product can never tell the user her order is fine on one screen and wrong on another.</para>
///
/// <para><b>It is a replacement-free structural op, and that is the honest granule.</b> The unit of
/// a reorder is the SECTION, not the character (senior-cto-advisor bind Q2, 2026-07-14). The
/// evidence names the observed and the recommended order — a real, readable diff — while the payload
/// carries NO CV content: only the user's own section headings. A Before/After of the raw text would
/// put two full copies of the CV on the wire to express an ordering, and would arm
/// <c>FromStructuralOp</c>'s Ordinal round-trip guard on messy real-world text — turning "your CV is
/// untidy" into an exception on the propose path, for exactly the CVs that most need the proposal.</para>
///
/// <para><b>STAGING-arm concept.</b> <c>CvImprovementContext</c> is built from a <c>ParsedResume</c>
/// (an imported file), which is why an observed order exists to disagree with. An app-managed CV is
/// emitted by the linearizer in canonical order BY CONSTRUCTION (ADR 0097 §2), so a reorder proposal
/// against it would be nonsense. Do not wire this transform to canonical content.</para>
/// </summary>
internal sealed class SectionReorderTransform : ICvTransform
{
    private readonly CvConventions _conventions;
    private readonly CvParsingLexiconData _lexicon;

    public SectionReorderTransform(CvConventions conventions, CvParsingLexiconData lexicon)
    {
        _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
    }

    public ProposedChangeKind Kind => ProposedChangeKind.SectionReorder;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context)
    {
        var order = SectionOrderAnalyzer.Analyze(context.RawText, _lexicon, _conventions);

        // A proposal that fires on a CV that is ALREADY correct is a vacuous proposal — the failure
        // mode this repo has paid for more than once. `Deviates` is also false when fewer than two
        // sections were recognised: one section cannot BE out of order.
        if (!order.Deviates)
        {
            yield break;
        }

        var evidence = new StructuralEvidence(
            $"Nuvarande ordning: {order.ObservedHeadings}. "
            + $"Rekommenderad ordning: {order.RecommendedHeadings}.");

        yield return ProposedChange.FromStructuralOp(
            targetId: "sectionorder:0",
            kind: ProposedChangeKind.SectionReorder,
            category: RubricCategory.Structure,
            criterionId: context.CriterionIdFor("B1"),
            evidence: evidence,
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.ReorderSection, "sektionsordning"),
            rationale: "Sortera sektionerna i den ordning ATS-system och rekryterare läser dem.",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.ReorderSection),
            pureTransform: null);
    }
}
