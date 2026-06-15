using Jobbliggaren.Application.Resumes.Improvement.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// B1 section order (Fas 4 STEG 10, F4-10) — NOT ASSESSED in v1. There is no machine-readable
/// rubric-recommended section order in the knowledge bank, and hardcoding an order in C# is
/// forbidden (CLAUDE.md §5: thresholds/orders live as versioned data, not inline). So this
/// transform proposes NOTHING until such a data source exists — an honest "ej bedömt v1", never
/// a fabricated reorder (parity the F4-9 NotAssessed discipline). The locked enum member +
/// transform exist so the contract is stable when the rubric gains a recommended order.
/// </summary>
internal sealed class SectionReorderTransform : ICvTransform
{
    public ProposedChangeKind Kind => ProposedChangeKind.SectionReorder;

    public IEnumerable<ProposedChange> Propose(CvImprovementContext context) =>
        [];
}
