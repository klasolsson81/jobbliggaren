using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// The result of a deterministic CV-improvement pass (Fas 4 STEG 10, F4-10; ADR 0071/0074).
/// Transient (CTO V-B = compute-on-demand, NO persistence, parity <c>CvReviewResult</c>).
/// Self-describing: carries the knowledge-bank versions it proposed against (cliché + verb
/// plain-string versions, rubric semantic version) so a future approve step is auditable. NO
/// opaque total (Goodhart, CLAUDE.md §5) — the explainable signal is the per-change diff list,
/// each with cited evidence + provenance.
/// </summary>
public sealed record CvImprovementResult(
    string ClicheListVersion,
    string VerbMappingVersion,
    RubricVersion RubricVersion,
    RenderProfile Profile,
    IReadOnlyList<ProposedChange> Changes);
