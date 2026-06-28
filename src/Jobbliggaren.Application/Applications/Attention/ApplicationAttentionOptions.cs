using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Applications.Attention;

/// <summary>
/// Operator-tunable thresholds for the /ansokningar "Kräver åtgärd"
/// attention-prioritisation rule (ADR 0085 §3). Application owns the contract;
/// Infrastructure binds it (<c>ApplicationAttention</c> section) with
/// <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> (parity with the
/// backfill/digest options). Repositioning the cadence is a config edit, not a
/// code change (CLAUDE.md §5 — no magic numbers).
///
/// <para>
/// <c>GhostedThresholdDays</c> is deliberately NOT here: signal 4 (no response
/// for long) reuses the existing per-aggregate <c>Application.GhostedThresholdDays</c>
/// field (default 21), projected into the read DTO. No new ghosted threshold is
/// introduced (ADR 0085 §3, "Default thresholds").
/// </para>
/// </summary>
public sealed class ApplicationAttentionOptions
{
    public const string SectionName = "ApplicationAttention";

    /// <summary>
    /// Signal 3 (proactive follow-up nudge): a submitted/acknowledged application
    /// surfaces once <c>now − AppliedAt ≥ FollowUpNudgeDays</c>. ADR 0085 default 7
    /// (disciplined short end of the published 7–14 day professional window);
    /// Klas may reposition to 5 via config without a code change.
    /// </summary>
    [Range(1, 365)]
    public int FollowUpNudgeDays { get; set; } = 7;

    /// <summary>
    /// Signal 5 (draft with approaching deadline): a <c>Draft</c> surfaces when the
    /// job ad's <c>ExpiresAt</c> is within <c>DraftDeadlineDays</c> and not yet
    /// passed. ADR 0085 default 5. Draft-only by design (the deadline is moot once
    /// submitted) — pinned by a dedicated unit test.
    /// </summary>
    [Range(1, 365)]
    public int DraftDeadlineDays { get; set; } = 5;
}
