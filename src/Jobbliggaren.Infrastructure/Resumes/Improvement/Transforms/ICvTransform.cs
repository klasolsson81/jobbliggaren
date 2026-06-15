using Jobbliggaren.Application.Resumes.Improvement.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

/// <summary>
/// One deterministic improvement concern (Fas 4 STEG 10, F4-10) — parity the F4-9
/// <c>ICriterionRule</c> map. A transform yields zero-or-more <see cref="ProposedChange"/>
/// (nothing to propose → nothing; never a fabricated edit, CLAUDE.md §5). Every emitted change
/// goes through the <see cref="ProposedChange"/> factory guards, so the no-synthesis invariant
/// holds by construction.
/// </summary>
internal interface ICvTransform
{
    /// <summary>The single change-kind this transform proposes.</summary>
    ProposedChangeKind Kind { get; }

    /// <summary>Proposes changes for the given parsed CV (deterministic, ordered).</summary>
    IEnumerable<ProposedChange> Propose(CvImprovementContext context);
}
