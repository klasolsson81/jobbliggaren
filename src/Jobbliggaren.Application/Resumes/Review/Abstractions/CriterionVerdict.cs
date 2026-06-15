namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The verdict for a single rubric criterion (Fas 4 STEG 9, F4-9, ADR 0071/0074).
/// The locked four-member set — parity with <c>MatchDimensionVerdict</c>:
/// <list type="bullet">
/// <item><see cref="Pass"/> — the criterion's pass-signal holds, cited evidence.</item>
/// <item><see cref="Warn"/> — partially met (between pass and fail), cited evidence.</item>
/// <item><see cref="Fail"/> — the criterion's fail-signal holds, cited evidence.</item>
/// <item><see cref="NotAssessed"/> — the determinism cannot assess it in v1 (pinned
/// "not assessed v1", missing input signal, or ad-dependent) — never a fabricated
/// Pass/Fail (CLAUDE.md §5 honesty contract). Carries a reason, no evidence.</item>
/// </list>
/// </summary>
public enum CriterionVerdict
{
    Pass,
    Warn,
    Fail,
    NotAssessed,
}
