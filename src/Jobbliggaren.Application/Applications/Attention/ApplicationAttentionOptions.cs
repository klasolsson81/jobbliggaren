using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// Operator-tunable thresholds for the /ansokningar "Kräver åtgärd"
/// attention-prioritisation rule (design §11 "Urgensregler", superseding ADR
/// 0085 §3; CTO-bound 2026-07-05). Application owns the contract; Infrastructure
/// binds it (<c>ApplicationAttention</c> section) with <c>ValidateDataAnnotations</c>
/// + <c>ValidateOnStart</c> (parity with the backfill/digest options).
/// Repositioning a threshold is a config edit, not a code change (CLAUDE.md §5 —
/// no magic numbers). Properties are declared in signal-priority order (3 → 4 → 5
/// → 6), matching <see cref="ApplicationAttentionSignal"/> and the evaluator.
///
/// <para>
/// The per-aggregate <c>Application.GhostedThresholdDays</c> is deliberately NOT
/// here and no longer feeds any attention signal. It drove the auto-ghost
/// detection job, which PR 4 retires (Klas: suggest-only ghosting). The
/// ghost-suggest SIGNAL now uses the operator option <see cref="GhostSuggestDays"/>
/// — a presentation threshold, decoupled from the former domain-behaviour field (SoC).
/// </para>
/// </summary>
public sealed class ApplicationAttentionOptions : IValidatableObject
{
    public const string SectionName = "ApplicationAttention";

    /// <summary>
    /// Signal 3 (draft with approaching deadline): a <c>Draft</c> surfaces when the
    /// job ad's <c>ExpiresAt</c> is within this many days and not yet passed. Design
    /// §11 "Utkast-deadline" default 7 (supersedes the provisional ADR 0085 default
    /// of 5). Draft-only by design (the deadline is moot once submitted) — pinned by
    /// a dedicated unit test.
    /// </summary>
    [Range(1, 365)]
    public int DraftDeadlineDays { get; set; } = 7;

    /// <summary>
    /// Signal 4 (ghost-suggest): a submitted/acknowledged application whose effective
    /// wait reaches this many days is offered for manual Ghosted marking. Design §11
    /// "Ghost-förslag" default 30. MUST be ≥ <see cref="NoResponseNudgeDays"/> for the
    /// nudge to remain reachable (the evaluator checks ghost-suggest first — see the
    /// <see cref="ApplicationAttentionSignal"/> priority note); enforced by
    /// <see cref="Validate"/> at start-up.
    /// </summary>
    [Range(1, 365)]
    public int GhostSuggestDays { get; set; } = 30;

    /// <summary>
    /// Signal 5 (no-response nudge): a submitted/acknowledged application surfaces
    /// once its effective wait (<c>now − max(LastStatusChangeAt, LastFollowUpAt)</c>,
    /// ADR 0092 D5) reaches this many days. Design §11 "Utan svar" default 14.
    /// </summary>
    [Range(1, 365)]
    public int NoResponseNudgeDays { get; set; } = 14;

    /// <summary>
    /// Signal 6 (silent after interview): an <c>Interviewing</c> application whose
    /// effective wait since the last status change (reset by a logged follow-up)
    /// reaches this many days surfaces for a nudge. Design §11 "Tyst efter intervju"
    /// default 7.
    /// </summary>
    [Range(1, 365)]
    public int SilentAfterInterviewDays { get; set; } = 7;

    /// <summary>
    /// Cross-field invariant: ghost-suggest is evaluated before the no-response nudge
    /// (its larger window subsumes the nudge's), so a configuration with
    /// <see cref="GhostSuggestDays"/> below <see cref="NoResponseNudgeDays"/> would
    /// make the nudge unreachable — a silent presentation defect. Fail fast at boot
    /// (invoked by <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>).
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GhostSuggestDays < NoResponseNudgeDays)
        {
            yield return new ValidationResult(
                $"{nameof(GhostSuggestDays)} ({GhostSuggestDays}) måste vara ≥ {nameof(NoResponseNudgeDays)} " +
                $"({NoResponseNudgeDays}) — annars blir no-response-nudgen onåbar (ghost-suggest kollas först).",
                [nameof(GhostSuggestDays), nameof(NoResponseNudgeDays)]);
        }
    }
}
