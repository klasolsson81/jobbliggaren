using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The result of a deterministic CV review (Fas 4 STEG 9, F4-9, ADR 0071/0074). A
/// transient Application read-projection (never persisted — senior-cto-advisor V-B =
/// compute-on-demand, parity <c>MatchScore</c>). Self-describing: it carries the
/// <see cref="RubricVersion"/> it was scored against (BUILD §8.1 "rubric_version stored
/// with each assessment" — held in-memory now, persisted at the LRM).
///
/// <para><b>Goodhart guard (CLAUDE.md §5, parity <c>MatchScore</c>).</b> There is NO
/// opaque total/score property — the explainable signal is the per-category verdict
/// COUNTS + bands (<see cref="Categories"/>), the full per-criterion <see cref="Verdicts"/>
/// list (each with cited evidence, Invariant 2), the separately-surfaced
/// <see cref="CriticalFails"/> (rubric critical-fail ids that FAILed, regardless of the
/// category sums), and the honest <see cref="AssessedCount"/>/<see cref="TotalCount"/>
/// (assessed excludes NotAssessed — so the user sees how much of the rubric the
/// determinism could actually assess for this CV).</para>
/// </summary>
public sealed record CvReviewResult(
    RubricVersion RubricVersion,
    RenderProfile Profile,
    IReadOnlyList<CvCategoryResult> Categories,
    IReadOnlyList<CvCriterionVerdict> Verdicts,
    IReadOnlyList<CvCriterionVerdict> CriticalFails,
    int AssessedCount,
    int TotalCount);
