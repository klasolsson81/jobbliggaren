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
    /// Daily bounds that to under 24 h, and <see cref="SweepCron"/> bounds it to one tick; this cron
    /// is the floor underneath that sweep, not a substitute for it.
    /// </para>
    ///
    /// <para>
    /// 05:30 UTC sits after <c>parsed-resume-retention</c> (05:15) and 30 minutes before the digest
    /// window (06:00), so a morning digest and a morning page load both read a set refreshed the same
    /// night. It is clear of the 02:00-05:00 UTC DB-contention window the SCB cadence was moved out of
    /// (#708 PR 2). The 15-minute lead-in is shorter than the house's usual 30, which the measured
    /// runtime affords: resolving and writing the widest bound-legal criterion is tens of
    /// milliseconds at either bound, so the whole run is seconds at any plausible corpus size. The
    /// figures live in the dated reports and never here -
    /// <c>docs/reviews/2026-09-06-1681-membership-measurement.md</c> for the 1 000 bound and
    /// <c>docs/reviews/2026-09-08-1706-bound-rederivation.md</c> for 2 500.
    /// </para>
    /// </summary>
    [Required]
    public string CadenceCron { get; set; } = "30 5 * * *";

    /// <summary>
    /// #1681 clause (ii) — cron for the RECONCILING SWEEP (UTC), the run that makes a user's own
    /// edit visible without waiting for <see cref="CadenceCron"/>. Default every minute.
    ///
    /// <para>
    /// <b>Two crons because there are two change-reasons, not two cadences of one job.</b>
    /// <see cref="CadenceCron"/> exists because the REGISTER moved — external input, weekly, and the
    /// accuracy enforcement point M-D6 was moved onto. This one exists because a PREDICATE moved —
    /// user input, continuous. They would not move together if either were re-derived.
    /// </para>
    ///
    /// <para>
    /// <b>Minutely is the scheduler's floor, not a judgement about tolerable latency</b>
    /// (senior-cto-advisor, 2026-09-07). Hangfire's recurring scheduler is cron-driven, so one minute
    /// is as fast as this mechanism goes; "as fast as the mechanism goes" is the only value here that
    /// is not a number someone chose. What the interval buys is LATENCY, never correctness — the
    /// sweep is correct at every interval, and a longer one only lengthens the window in which the
    /// surface honestly says it does not know. That is what separates this from a delay chosen to
    /// land "probably after the commit", which would buy correctness with a guess and is refused.
    /// </para>
    ///
    /// <para>
    /// A tick that collides with the nightly run simply waits for it — both jobs hold the SAME
    /// distributed lock (see <c>CompanyWatchCriterionMaterialisationWorker</c>) — and loses no work:
    /// the sweep is stateless and the next tick re-derives the same candidate set from committed
    /// state.
    /// </para>
    /// </summary>
    [Required]
    public string SweepCron { get; set; } = "* * * * *";

    /// <summary>
    /// #1681 clause (ii) — how many stale criteria ONE sweep tick resolves. Default 50.
    ///
    /// <para>
    /// <b>COMPUTED from the measurement, not chosen — and RE-COMPUTED whenever
    /// <c>CompanyWatchCriterionMember.MaxPerCriterion</c> moves, because both of its terms are
    /// functions of the member count</b> (security-auditor, 2026-09-08: this value does not inherit
    /// across a bound change). The constraint is that a tick's work stay far below its own interval,
    /// so a backlog drains across ticks instead of a tick overrunning the next. Re-computed for the
    /// 2 500 bound in <c>docs/reviews/2026-09-08-1706-bound-rederivation.md</c>, which carries both
    /// arithmetics side by side: a tick's 50 criteria take <b>under a tenth</b> of the 60 s interval at
    /// the new bound, as they did at the old one. That is why 50 SURVIVES the re-derivation rather
    /// than being inherited through it — the per-criterion cost was re-measured, not assumed.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>That figure ADDS two terms taken on different instruments</b> (dotnet-architect,
    /// 2026-09-08, on the report this one reads beside): the replace half is a p95 on a throwaway
    /// fixture and the selection half a p50 against dev, so each cell is a composed estimate rather
    /// than a quantile. The conclusion is insensitive to that — the headroom is an order of magnitude
    /// — but the reading is not a p95 of the tick and must not be quoted as one.
    /// </para>
    ///
    /// <para>
    /// A second property falls out and is worth naming: 50 is 2,5x
    /// <c>CompanyWatchCriterion.MaxPerUser</c> (20), so ONE user editing every watch she owns can
    /// never fill a batch and can never delay another user's create past the following tick.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>This value and <see cref="SweepCron"/> are one decision</b>, exactly as
    /// <see cref="MaxReadAgeHours"/> and <see cref="CadenceCron"/> are: change the interval and the
    /// arithmetic above must be re-run against the new period.
    /// </para>
    /// </summary>
    [Range(1, 10_000)]
    public int SweepBatchSize { get; set; } = 50;

    /// <summary>
    /// Criteria loaded per page. Large enough that the page count stays trivial at any plausible
    /// corpus, small enough that one page is a bounded allocation even when every row carries the
    /// maximum two text[] axes.
    ///
    /// <para>
    /// <b>Configurable for ONE reason: so the paging loop is reachable by a test</b> (test-writer,
    /// 2026-09-06). At the shipped 500 no fixture could ever produce a second page, which left the
    /// loop's termination and totality entirely unmeasured - and two of the surviving mutants there
    /// (dropping the offset advance, dropping the empty-page break) are INFINITE LOOPS in operation,
    /// while a third (dropping the ORDER BY) silently yields wrong member sets. A knob that exists
    /// only to make a hazard observable is worth more than the tidiness of a constant.
    /// </para>
    /// </summary>
    [Range(1, 10_000)]
    public int CriterionPageSize { get; set; } = 500;

    /// <summary>
    /// #1681 part 2 (security-auditor Major 2) — how old a materialisation may be before the READ
    /// side stops believing it and answers "not known yet" instead.
    ///
    /// <para>
    /// <b>Without this, a criterion that fails every run forever serves stale exact numbers with no
    /// signal.</b> <c>CompanyWatchCriterionMaterialiser</c> catches per-criterion exceptions, logs and
    /// continues, and a run counts as successful as long as at least one criterion succeeded — by
    /// design, so one bad criterion cannot stop the batch. The consequence is that a permanently
    /// failing criterion is invisible from the read side: its row keeps its old <c>Materialised</c>
    /// state and its old member set, and the surface renders an exact number for a set nobody has
    /// refreshed. security-auditor's part-1 condition Major 3 was *"a missing OR STALE materialisation
    /// degrades honestly, never to a silent number"*; part 1 answered "missing" (no row →
    /// <c>NotMaterialised</c>), and the read path is where "stale" is decided.
    /// </para>
    ///
    /// <para>
    /// <b>It introduces no fifth state.</b> An over-age row degrades to the SAME
    /// <c>NotMaterialised</c> arm the absent row and the mismatched fingerprint already use — three
    /// triggers, one honest answer. The closed hierarchy is untouched.
    /// </para>
    ///
    /// <para>
    /// <b>DERIVED from <see cref="CadenceCron"/>, not chosen.</b> At the default daily cadence a
    /// single missed write is a transient failure that the next run repairs, so a bound of one day
    /// would fire on ordinary noise. Three consecutive daily misses is not noise — it is a criterion
    /// the job cannot process, and nothing about waiting longer will fix it. Hence 72 h = 3 cadence
    /// periods. ⚠ <b>The two values are one decision:</b> if <see cref="CadenceCron"/> moves, this
    /// must be re-derived against the new period, which is why they sit in the same options section
    /// rather than one being a constant somewhere else. A cron cannot be turned into a period without
    /// a parser, and this project takes no dependency on one for a bound a human sets deliberately.
    /// </para>
    ///
    /// <para>
    /// The bound is applied IN the SQL gate, beside the fingerprint comparison, so an over-age row
    /// costs no ad scan at all — the same reason the fingerprint comparison is not done in C#.
    /// </para>
    /// </summary>
    [Range(1, 8_760)]
    public int MaxReadAgeHours { get; set; } = 72;
}
