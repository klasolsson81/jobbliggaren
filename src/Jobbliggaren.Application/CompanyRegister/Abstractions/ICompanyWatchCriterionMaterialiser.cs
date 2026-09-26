namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #1681 (ADR 0139) — the use-case port for resolving every saved smart watch (a PREDICATE) into the
/// set of register companies it matches (an org.nr SET), out of the request path.
///
/// <para>
/// <b>Two run methods, because there are two reasons a membership goes out of date</b>
/// (senior-cto-advisor, 2026-09-07). <see cref="MaterialiseAsync"/> exists because the REGISTER
/// moved — external input, weekly, and it is M-D6's accuracy enforcement point.
/// <see cref="MaterialiseChangedAsync"/> exists because a PREDICATE moved — user input, continuous.
/// Two actors, two reasons to change, so two methods; a single method with a selection flag would
/// hide both inside one. Both are invoked by the Worker's own recurring Hangfire jobs, under ONE
/// shared distributed lock — see <c>CompanyWatchCriterionMaterialisationWorker</c> for why that
/// sharing is a correctness requirement and not hygiene.
/// </para>
///
/// <para>
/// <b>Why this exists at all</b> (ADR 0139, one sentence): a company watch stores an org.nr, a smart
/// criterion stores a predicate. Resolving a predicate needs a join against <c>company_register</c>
/// (1 066 938 rows), measured at <b>6 556 ms</b> for the widest bound-legal criterion against
/// <c>/oversikt</c>'s 300 ms p95 budget. The join's two halves change at radically different rates —
/// criterion → org.nr weekly, org.nr → ads constantly — so recomputing the weekly half on every page
/// load is the defect and 6 556 ms is only the symptom.
/// </para>
///
/// <para>
/// <b>The port lives in Application; nothing it returns carries an org.nr.</b> The result is counts
/// only (see <see cref="CompanyWatchCriterionMaterialisationResult"/>). The membership itself is
/// written to an Infrastructure-internal table that is NOT a <c>DbSet</c> on <c>IAppDbContext</c> —
/// the DPIA C-D4 / M-C5 firewall holds VERBATIM under this form, which is why
/// <c>security-auditor</c>'s Blocker 1 and Major 2 never arise (2026-09-06). Putting the member table
/// on the Application port would instead have required a DPIA Part D amendment AND a widened firewall
/// guard, because <c>ScbCompanyRegisterLayerTests.IAppDbContext_exposes_only_Domain_types</c> would
/// have PASSED on a Domain-typed member entity while the property it documents became false — the
/// vacuous-guarantee class this repo names twice (#805-3, #842).
/// </para>
///
/// <para>
/// The Worker wrapper stays a thin <c>DisableConcurrentExecution</c> shell that never sees the
/// mechanics (Clean Arch, ADR 0023 delbeslut 2) — parity <see cref="IScbCompanyRegisterRefresher"/>.
/// </para>
/// </summary>
public interface ICompanyWatchCriterionMaterialiser
{
    /// <summary>
    /// Recomputes the membership of EVERY saved criterion: resolve → breadth-gate → personnummer
    /// filter → replace. Idempotent, and idempotent in the strong sense that matters here — each
    /// criterion's set is REPLACED, never supplemented (<c>security-auditor</c> Major 5c), so a
    /// criterion the user narrowed stops counting the companies she dropped.
    ///
    /// <para>
    /// <b>This is also M-D6's named replacement</b> (Major 3). The DPIA's accuracy mitigation used to
    /// be structural — <c>CompanyWatchBrowseQuery.FromWhere</c> carries <c>status = @status</c> in
    /// positive polarity, so a de-registered company could not be surfaced. Under materialisation
    /// that predicate is off the read path, so the enforcement point moves HERE: every run rebuilds
    /// each set from <c>status = 'Active'</c> alone, which removes a de-registered company by
    /// CONSTRUCTION rather than by a sweep that could be forgotten. It is a full recompute, never a
    /// delta — there is no "remove the dead ones" step to get wrong.
    /// </para>
    /// </summary>
    Task<CompanyWatchCriterionMaterialisationResult> MaterialiseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// #1681 clause (ii) — recomputes only those criteria whose stored membership does not describe
    /// their CURRENT predicate: a criterion just created, or one whose SNI/kommun axes were edited.
    /// Same resolve → breadth-gate → personnummer filter → replace per criterion, and the same
    /// REPLACE semantics (<c>security-auditor</c> Major 5c), so nothing about a criterion's outcome
    /// depends on which run reached it.
    ///
    /// <para>
    /// <b>The committed state IS the queue, and that is the design</b> (senior-cto-advisor,
    /// 2026-09-07). No handler enqueues anything, no signal is emitted, and no outbox row is written.
    /// The knowledge piece — <i>this criterion's membership is not current for its predicate</i> — is
    /// already durably in the database as <c>criteria_fingerprint</c> beside <c>materialised_at</c>,
    /// which is the same fact the READ path already gates on. A second representation of one
    /// knowledge piece is what DRY forbids, and it would buy nothing: this form is
    /// level-triggered, so a missed pass is repaired by the next one, whereas any edge-triggered
    /// signal must additionally defeat the fact that <c>UnitOfWorkBehavior</c> commits AFTER the
    /// handler returns — a signal raised before its own commit either finds no row (create) or reads
    /// the OLD predicate (edit).
    /// </para>
    ///
    /// <para>
    /// <b>A rename must never reach a register resolution</b>, and this method is where that holds.
    /// The candidate query prefilters on <c>updated_at</c>, which <c>Rename</c> bumps exactly as
    /// <c>UpdateCriteria</c> does — so it is a SUPERSET, never the test. The decision is
    /// <see cref="Jobbliggaren.Application.CompanyWatches.Abstractions.CriteriaFingerprint"/>,
    /// compared after loading: a renamed criterion costs one row read and one SHA-256, and is then
    /// skipped.
    /// </para>
    ///
    /// <para>
    /// Reports the same <see cref="CompanyWatchCriterionMaterialisationResult"/>. In steady state
    /// that is a run with <c>CriteriaSeen = 0</c> — which must stay distinguishable from a run in
    /// which every criterion failed, and is what <c>CriteriaFailed</c> is read against.
    /// </para>
    /// </summary>
    Task<CompanyWatchCriterionMaterialisationResult> MaterialiseChangedAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// #1681 — aggregate outcome of one materialisation run. Counts only: no org.nr, no criterion label,
/// nothing user-identifying, so the whole record is safe to log and to audit (ADR 0087 D8(c)).
/// </summary>
/// <param name="CriteriaSeen">Criteria the run RESOLVED against the register. For
/// <c>MaterialiseAsync</c> that is every saved criterion, since it considers and resolves the same
/// set. For <c>MaterialiseChangedAsync</c> it is narrower than the set considered: a candidate the
/// fingerprint dismisses (a rename) is deliberately not counted, which is what makes
/// <c>CriteriaSeen == 0</c> the assertion that no register resolution happened. Do not "correct" it
/// to count candidates — a test pins the distinction.</param>
/// <param name="CriteriaMaterialised">Criteria whose company set fitted under the breadth gate and was
/// written.</param>
/// <param name="CriteriaTooBroad">Criteria REFUSED by the breadth gate — stored with no members and a
/// <c>TooBroad</c> state, so the read side renders a refusal rather than a number it cannot back.</param>
/// <param name="MembersWritten">Total member rows written across every materialised criterion.</param>
/// <param name="MembersExcludedPersonnummerShaped">Candidate org.nr dropped at THIS job's own write
/// boundary by <c>OrganizationNumber.IsPersonnummerShaped()</c> (<c>security-auditor</c> Major 4).
/// Expected 0 — the register is legal-entities-only by ADR 0091 — but the count is the audited proof
/// that the member table's pnr-freedom is its OWN invariant and not one inherited from another
/// subsystem's ingest, which is exactly what the repo declined to do for <c>CompanyLookupDto</c>
/// (#454).</param>
/// <param name="MembersExcludedInvalid">Candidate org.nr dropped because they failed 10-digit
/// validation.</param>
/// <param name="CriteriaFailed">Criteria whose own materialisation threw and was skipped. A partial
/// failure is survivable - one corrupt criterion must not deny every other user a fresh membership -
/// but it must be VISIBLE, and a count on the result is what makes it so. If EVERY criterion failed,
/// the run THROWS instead of returning: a run that wrote nothing and reported success is
/// indistinguishable from a run that had nothing to do (dotnet-architect, 2026-09-06).</param>
/// <param name="StartedAt">Run start (from <c>IDateTimeProvider</c>).</param>
/// <param name="CompletedAt">Run completion (from <c>IDateTimeProvider</c>).</param>
public sealed record CompanyWatchCriterionMaterialisationResult(
    int CriteriaSeen,
    int CriteriaMaterialised,
    int CriteriaTooBroad,
    int MembersWritten,
    int MembersExcludedPersonnummerShaped,
    int MembersExcludedInvalid,
    int CriteriaFailed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
