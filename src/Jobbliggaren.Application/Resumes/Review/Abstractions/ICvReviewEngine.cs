namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// Reviews a CV against the versioned knowledge-bank rubric (Fas 4 STEG 9, F4-9,
/// ADR 0071/0074; unified input Fas 4b PR-4, ADR 0093 §D8). Deterministic — NO AI/LLM:
/// every verdict is a rule over the reviewable content + the rubric/cliché/verb data,
/// with cited evidence (Invariant 2).
///
/// <para>The engine takes a <see cref="CvReviewContext"/> built by one of its two pure
/// adapters (<see cref="CvReviewContext.FromParsed"/> pre-promote,
/// <see cref="CvReviewContext.FromCanonical"/> post-promote) — ONE rubric engine, one
/// assessment path, regardless of which aggregate supplied the content (D8). Any CV-PII
/// in the context was decrypted by the field-decryption interceptor when the caller
/// loaded the aggregate inside the warmed field-encryption pipeline (Invariant 3) — the
/// engine itself never touches the DbContext, the DEK pipeline, or any logger, so it
/// stays a pure, 100%-unit-testable function of (CV, rubric). A5 (career progression) +
/// C1 (genuine grammar) and every layout/ad-dependent criterion report
/// <see cref="CriterionVerdict.NotAssessed"/> in v1 (honest, never mis-reported —
/// CLAUDE.md §5, OQ3).</para>
/// </summary>
public interface ICvReviewEngine
{
    ValueTask<CvReviewResult> ReviewAsync(
        CvReviewContext context,
        RenderProfile profile,
        CancellationToken cancellationToken);
}
