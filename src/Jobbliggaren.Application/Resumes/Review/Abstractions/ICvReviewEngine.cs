using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// Reviews a parsed CV against the versioned knowledge-bank rubric (Fas 4 STEG 9, F4-9,
/// ADR 0071/0074). Deterministic — NO AI/LLM: every verdict is a rule over the parsed CV
/// + the rubric/cliché/verb data, with cited evidence (Invariant 2).
///
/// <para>The engine takes an ALREADY-MATERIALISED <see cref="ParsedResume"/> (its CV-PII
/// is decrypted by the field-decryption interceptor when the caller loads it inside the
/// warmed field-encryption pipeline, Invariant 3) — the engine itself never touches the
/// DbContext, the DEK pipeline, or any logger, so it stays a pure, 100%-unit-testable
/// function of (CV, rubric). A5 (career progression) + C1 (genuine grammar) and every
/// layout/ATS/Visual/ad-dependent criterion report <see cref="CriterionVerdict.NotAssessed"/>
/// in v1 (honest, never mis-reported — CLAUDE.md §5, OQ3).</para>
/// </summary>
public interface ICvReviewEngine
{
    ValueTask<CvReviewResult> ReviewAsync(
        ParsedResume parsedResume,
        RenderProfile profile,
        CancellationToken cancellationToken);
}
