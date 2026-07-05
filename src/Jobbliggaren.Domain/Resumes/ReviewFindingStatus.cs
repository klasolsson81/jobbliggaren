using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The user's decision on one review finding (Fas 4b PR-4, ADR 0093 §D2(e); handoff §5.3
/// "åtgärdad / ignorerad / öppen"). A closed, system-controlled vocabulary → SmartEnum,
/// not a string (CLAUDE.md §5), persisted by Name-string (reorder-safe house default).
/// Staleness ("the CV changed after this decision") is deliberately NOT a fourth value —
/// it rides orthogonally as <see cref="ResumeFindingStatus.StaleAt"/> so the user's
/// decision is never destroyed (CTO-bind PR-4 Q3).
/// </summary>
public sealed class ReviewFindingStatus : SmartEnum<ReviewFindingStatus>
{
    /// <summary>Öppen — the default; no user decision recorded (also the revert target).</summary>
    public static readonly ReviewFindingStatus Open = new(nameof(Open), 0);

    /// <summary>Åtgärdad — "jag fixar det själv, markera som klar". Goes stale on content change.</summary>
    public static readonly ReviewFindingStatus Resolved = new(nameof(Resolved), 1);

    /// <summary>
    /// Ignorerad — "ignorera regeln" (style criteria, handoff §5.3). A rule-scoped,
    /// content-independent opt-out: it never goes stale; its natural invalidation is the
    /// rubric-version key boundary (a new rubric version starts at <see cref="Open"/>).
    /// </summary>
    public static readonly ReviewFindingStatus Ignored = new(nameof(Ignored), 2);

    private ReviewFindingStatus(string name, int value) : base(name, value) { }
}
