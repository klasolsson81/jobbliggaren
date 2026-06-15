using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.Resumes.Review.Abstractions;

/// <summary>
/// The verdict for one rubric criterion with its cited evidence (Fas 4 STEG 9, F4-9).
/// A private ctor + two static factories make the ADR 0074 Invariant 2 guard
/// UNAVOIDABLE — the determinism cannot mint an assessed Pass/Warn/Fail without cited
/// evidence, and cannot mislabel a "not assessed v1" criterion as assessed (CLAUDE.md §5).
/// Carries NO numeric score (Goodhart) and NO weight/critical flag — weighting and
/// critical-fail surfacing are the engine's/result's concern, not the verdict's.
/// </summary>
public sealed record CvCriterionVerdict
{
    /// <summary>The rubric criterion id (e.g. "A1", "B4", "D6").</summary>
    public string CriterionId { get; }

    /// <summary>The rubric category the criterion belongs to.</summary>
    public RubricCategory Category { get; }

    /// <summary>Pass / Warn / Fail / NotAssessed.</summary>
    public CriterionVerdict Verdict { get; }

    /// <summary>The cited evidence (≥1 for an assessed verdict; empty for NotAssessed).</summary>
    public IReadOnlyList<CitedEvidence> Evidence { get; }

    /// <summary>The honest reason a criterion is NotAssessed (null for an assessed verdict).</summary>
    public string? NotAssessedReason { get; }

    private CvCriterionVerdict(
        string criterionId,
        RubricCategory category,
        CriterionVerdict verdict,
        IReadOnlyList<CitedEvidence> evidence,
        string? notAssessedReason)
    {
        CriterionId = criterionId;
        Category = category;
        Verdict = verdict;
        Evidence = evidence;
        NotAssessedReason = notAssessedReason;
    }

    /// <summary>
    /// An assessed verdict (Pass/Warn/Fail) that MUST cite ≥1 piece of evidence
    /// (Invariant 2). Throws if <paramref name="verdict"/> is
    /// <see cref="CriterionVerdict.NotAssessed"/> or if <paramref name="evidence"/> is
    /// null/empty — the load-bearing honesty guard.
    /// </summary>
    public static CvCriterionVerdict Assessed(
        string criterionId,
        RubricCategory category,
        CriterionVerdict verdict,
        IReadOnlyList<CitedEvidence> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(criterionId);

        if (verdict is CriterionVerdict.NotAssessed)
        {
            throw new ArgumentException(
                "Use NotAssessed(...) for a NotAssessed verdict; Assessed(...) is for " +
                "Pass/Warn/Fail only (so a 'not assessed v1' criterion can never be " +
                "smuggled in carrying fabricated evidence).",
                nameof(verdict));
        }

        if (evidence is null || evidence.Count == 0)
        {
            throw new ArgumentException(
                $"Verdict {verdict} for criterion '{criterionId}' must cite at least one " +
                "piece of evidence (ADR 0074 Invariant 2: no CV verdict without cited evidence).",
                nameof(evidence));
        }

        return new CvCriterionVerdict(criterionId, category, verdict, evidence, notAssessedReason: null);
    }

    /// <summary>
    /// A NotAssessed verdict carrying an honest structural reason and no evidence —
    /// the only verdict permitted zero evidence (it asserts nothing, so it grounds nothing).
    /// </summary>
    public static CvCriterionVerdict NotAssessed(
        string criterionId,
        RubricCategory category,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(criterionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new CvCriterionVerdict(criterionId, category, CriterionVerdict.NotAssessed, [], reason);
    }
}
