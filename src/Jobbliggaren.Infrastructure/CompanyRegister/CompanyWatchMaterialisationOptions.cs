using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>
/// #1681 (ADR 0139) — configuration for the criterion-membership materialisation job. Bound from the
/// <c>CompanyWatchMaterialisation</c> section; DataAnnotations-validated and validated on start
/// (parity <see cref="ScbRegisterOptions"/>).
///
/// <para>
/// <b>This is a SEPARATE options section from <see cref="ScbRegisterOptions"/>, and the separation is
/// the finding, not a tidiness preference</b> (security-auditor Major 3, 2026-09-06). The obvious
/// design — recompute membership at the end of a register sync — ties the job to
/// <c>ScbRegister:Enabled</c>, which <b>defaults false</b> (<c>ScbRegisterOptions.Enabled</c>: the
/// real population is a deliberate, cert-gated, DPIA-cleared action). In the default posture the
/// materialisation would then never run at all: the only refresh would be the user's own edit, and a
/// company that SCB de-registered would keep being counted on her <c>/oversikt</c> indefinitely. That
/// is DPIA R-D6 (Art. 5(1)(d) accuracy) with its stated mitigation removed — which is precisely the
/// mitigation this job was made to replace, so inheriting the gate would have voided the thing it was
/// built for.
/// </para>
///
/// <para>
/// Hence a section of its own, an <see cref="Enabled"/> of its own, and a cron of its own. Nothing
/// here reads <c>ScbRegister:*</c>.
/// </para>
/// </summary>
public sealed class CompanyWatchMaterialisationOptions
{
    public const string SectionName = "CompanyWatchMaterialisation";

    /// <summary>
    /// Master switch. <b>Default TRUE — the inverse of <see cref="ScbRegisterOptions.Enabled"/>, and
    /// the inversion is deliberate.</b> That switch guards a metered, certificate-gated call to an
    /// external authority, so its safe default is off. This one guards a purely LOCAL recompute over
    /// tables we already hold: no external call, no credential, no cost beyond a few milliseconds per
    /// criterion. Defaulting it false would re-create Major 3's defect one level down — an
    /// independent trigger that is independently switched off is not an independent trigger.
    ///
    /// <para>
    /// The kill-switch exists anyway (house discipline: a new bounded write path ships with a toggle),
    /// and turning it off degrades HONESTLY rather than silently: existing state rows go stale and
    /// carry their <c>MaterialisedAt</c> stamp saying so, while a criterion created afterwards has no
    /// state row at all — which the read side must render as "not known yet", never as a zero.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron for the recurring materialisation (UTC). Default daily 05:30.
    ///
    /// <para>
    /// <b>Daily, although the register only changes weekly</b> — because the cadence must cover BOTH
    /// inputs, and they move at different rates. The register moves on
    /// <c>ScbRegister:SyncCadenceCron</c> (Saturday 06:00 UTC, an ~11 h run), so a daily job picks up
    /// a completed sync on the Sunday morning after it finishes. But the SAVED CRITERIA move whenever
    /// a user creates or edits one, which is continuous — and a criterion created on a Monday would
    /// wait until the following weekend under a weekly cadence, showing "not known yet" for six days.
    /// Daily bounds that to under 24 h. (An immediate recompute on create/edit is #1681 part 2's, and
    /// it makes the user's OWN change visible at once; this cron is the floor underneath it, not a
    /// substitute for it.)
    /// </para>
    ///
    /// <para>
    /// 05:30 UTC sits after <c>parsed-resume-retention</c> (05:15) and 30 minutes before the digest
    /// window (06:00), so a morning digest and a morning page load both read a set refreshed the same
    /// night. It is clear of the 02:00-05:00 UTC DB-contention window the SCB cadence was moved out of
    /// (#708 PR 2). The 15-minute lead-in is shorter than the house's usual 30, which the measured
    /// runtime affords: the widest bound-legal criterion costs 11-30 ms to resolve and 30,67 ms p95 to
    /// write, so the whole run is seconds at any plausible corpus size
    /// (docs/reviews/2026-09-06-1681-membership-measurement.md).
    /// </para>
    /// </summary>
    [Required]
    public string CadenceCron { get; set; } = "30 5 * * *";
}
